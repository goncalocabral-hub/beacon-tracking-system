BLE Beacon Monitor

A BLE Beacon Monitoring System designed to detect Bluetooth Low Energy beacons, collect RSSI signal data and visualize beacon presence in real time through a web dashboard.
This project simulates a real-world IoT monitoring system, combining a BLE scanner, a backend server, and a web dashboard for visualization.

Architecture
The system is composed of three main components:
1. BLE Scanner (C#)
- Scans for BLE advertisements
- Filters specific beacon identifiers
- Extracts RSSI signal strength
- Sends data to the backend server

2. Backend Server (ASP.NET)
- Receives beacon data via HTTP endpoint /ingest
- Stores the latest state of detected beacons
- Exposes a /state API endpoint
- Serves the web dashboard

3. Web Dashboard
- Displays detected beacons in real time
- Visualizes RSSI values
- Shows beacon activity on a 2D map


System Architecture Diagram

BLE Beacons
      │
      │ (Bluetooth Advertisement)
      ▼
BLE Scanner (C#)
      │
      │ HTTP POST
      ▼
Beacon Server (ASP.NET)
      │
      ├── /ingest
      ├── /state
      │
      ▼
Web Dashboard


Features

- BLE beacon detection
- RSSI signal strength monitoring
- Real-time beacon visualization
- HTTP API for beacon ingestion
- Web-based monitoring dashboard
- Modular architecture

Technologies Used

- C#
- .NET / ASP.NET
- Bluetooth Low Energy (BLE)
- JavaScript
- HTML / CSS
- REST API


Future Improvements

- Database storage (PostgreSQL / MongoDB)
- WebSocket real-time updates
- Beacon location triangulation
- Historical signal analytics
- Multi-scanner support

Author - Gonçalo Cabral

Student in Computer Science and Organizational Communication
Interested in Cybersecurity, Networking and Systems Engineering
