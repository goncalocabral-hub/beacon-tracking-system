// See https://aka.ms/new-console-template for more information
//Console.WriteLine("Hello, World!");


using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;

class Program
{
    // ======= SERVER (API) =======
    private const string SERVER_URL = "http://localhost:5209/ingest";// muda se a tua porta for outra
    private const string RECEIVER_ID = "PC1";

    // DEV only: aceitar certificado HTTPS local
    private static readonly HttpClient Http = new HttpClient(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    // ======= MODO =======
    // true  -> Descobrir keys (vai imprimir as keys novas)
    // false -> Só mostrar beacons conhecidos (dicionário BeaconNames)
    private const bool DISCOVERY = false;

    // Quantos bytes do início do payload entram na key (ajusta se precisares)
    private const int KeyHeadBytes = 4; // começa em 4 (bate com as tuas "27_0109..." / "27_010F...")
    // Se vires colisões (2 beacons a dar a mesma key), sobe para 6 ou 8.

    private const int WindowSize = 20;
    private const int RefreshSeconds = 2;
    private const int OfflineAfterSeconds = 10;

    private const int NearThreshold = -55;
    private const int MidThreshold = -75;

    private static readonly object LockObj = new();

    // ======= BEACONS CONHECIDOS (só usado quando DISCOVERY=false) =======
    private static readonly Dictionary<string, string> BeaconNames = new()
    {
        {"27_010F2022", "TB15-1 #1"},
        {"27_01092002", "TB15-1 #2"},
        {"27_01092026", "SB18-3 #1"},
        {"27_01092022", "SB18-3 #2"},
        {"18_0109212A", "CardTag_CT18-3"}
    };

    class BeaconState
    {
        public string StableKey = "";
        public DateTime LastSeenUtc = DateTime.MinValue;
        public short LastRssi = 0;
        public Queue<short> RssiWindow = new();
        public int SeenCount = 0;

        public double MedianRssi
        {
            get
            {
                if (RssiWindow.Count == 0) return -999;

                var arr = RssiWindow.Select(x => (double)x).OrderBy(x => x).ToArray();
                int mid = arr.Length / 2;
                return arr.Length % 2 == 1 ? arr[mid] : (arr[mid - 1] + arr[mid]) / 2.0;
            }
        }
    }

    private static readonly Dictionary<string, BeaconState> States =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PrintedNew =
        new(StringComparer.OrdinalIgnoreCase);

    static void Main()
    {
        Console.WriteLine("Kontakt.io scanner (stable keys) ...\n");
        Console.WriteLine($"[POST] A enviar para: {SERVER_URL}");
        Console.WriteLine($"[RX ] ReceiverId: {RECEIVER_ID}\n");

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        watcher.Received += (_, a) =>
        {
            bool processedKontakt = false;

            // Procura ManufacturerData da Kontakt (CompanyId=6)
            foreach (var md in a.Advertisement.ManufacturerData)
            {
                if (md.CompanyId != 6) continue;

                byte[] payload = md.Data.ToArray();
                if (payload.Length == 0) return;

                string stableKey = BuildStableKey(payload, KeyHeadBytes);

                // DISCOVERY: imprime keys novas que aparecem
                if (DISCOVERY)
                {
                    lock (LockObj)
                    {
                        if (!PrintedNew.Contains(stableKey))
                        {
                            PrintedNew.Add(stableKey);
                            Console.WriteLine($"[NOVO] {stableKey}  RSSI={a.RawSignalStrengthInDBm}");
                        }
                    }
                }
                else
                {
                    // Modo filtrado: só os que estão no dicionário
                    if (!BeaconNames.ContainsKey(stableKey))
                        return;
                }

                // Atualiza estado (para render no console)
                lock (LockObj)
                {
                    if (!States.TryGetValue(stableKey, out var st))
                    {
                        st = new BeaconState { StableKey = stableKey };
                        States[stableKey] = st;
                    }

                    st.LastSeenUtc = DateTime.UtcNow;
                    st.LastRssi = a.RawSignalStrengthInDBm;
                    st.SeenCount++;

                    st.RssiWindow.Enqueue(a.RawSignalStrengthInDBm);
                    while (st.RssiWindow.Count > WindowSize) st.RssiWindow.Dequeue();
                }

                // Envia para o servidor sem bloquear o watcher (fire-and-forget)
                SendToServerAsync(stableKey, a.RawSignalStrengthInDBm);

                processedKontakt = true;
                break; // só processamos um ManufacturerData Kontakt por anúncio
            }

            if (!processedKontakt)
                return;
        };

        watcher.Start();

        while (true)
        {
            Render();
            Thread.Sleep(TimeSpan.FromSeconds(RefreshSeconds));
        }
    }

    private static async Task SendToServerAsync(string beaconId, int rssi)
    {
        try
        {
            var payload = new
            {
                receiverId = RECEIVER_ID,
                beaconId = beaconId,
                rssi = rssi
            };

            using var resp = await Http.PostAsJsonAsync(SERVER_URL, payload).ConfigureAwait(false);

            // Se quiseres debugar falhas HTTP:
            // if (!resp.IsSuccessStatusCode)
            //     Console.WriteLine($"[POST ERR] {beaconId} => {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[POST FAIL] {beaconId}: {ex.Message}");
        }
    }

    private static void Render()
    {
        List<BeaconState> snapshot;
        lock (LockObj) snapshot = States.Values.ToList();

        var now = DateTime.UtcNow;

        Console.Clear();
        Console.WriteLine($"Kontakt.io | {(DISCOVERY ? "DISCOVERY" : "FILTERED")} | KeyHeadBytes={KeyHeadBytes} | Refresh={RefreshSeconds}s");
        Console.WriteLine($"Server: {SERVER_URL}\n");

        Console.WriteLine($"{"NAME",-18} {"KEY",-40} {"RSSI",-6} {"MED",-6} {"ZONE",-6} {"STATUS",-8} {"SEEN",-4}");
        Console.WriteLine(new string('-', 95));

        foreach (var s in snapshot.OrderByDescending(x => x.MedianRssi))
        {
            double seenAgo = (now - s.LastSeenUtc).TotalSeconds;
            bool online = seenAgo <= OfflineAfterSeconds;

            string zone = GetZone(s.MedianRssi);

            string name = DISCOVERY
                ? "(novo)"
                : (BeaconNames.TryGetValue(s.StableKey, out var n) ? n : "(desconhecido)");

            Console.WriteLine($"{Trunc(name, 18),-18} {Trunc(s.StableKey, 40),-40} {s.LastRssi,-6} {s.MedianRssi,-6:F1} {zone,-6} {(online ? "ONLINE" : "OFFLINE"),-8} {s.SeenCount,-4}");
        }

        if (DISCOVERY)
        {
            Console.WriteLine("\nDISCOVERY: copia as keys [NOVO] para o dicionário BeaconNames e depois mete DISCOVERY=false.");
        }
    }

    private static string GetZone(double rssi)
    {
        if (rssi >= NearThreshold) return "NEAR";
        if (rssi >= MidThreshold) return "MID";
        return "FAR";
    }

    // StableKey no formato: "<len>_<headHex>"
    private static string BuildStableKey(byte[] payload, int headBytes)
    {
        int len = payload.Length;
        int n = Math.Min(headBytes, len);

        string headHex = BitConverter.ToString(payload.Take(n).ToArray()).Replace("-", "");
        return $"{len}_{headHex}";
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
}