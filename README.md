# Oscilla
local music player-Try avoid making trash


Oscilla is a local music player powered by Naduio and engineered for ultimate purity and high performance. Zero bloatware, pure focus on sound and efficiency.

---

## ✨ Key Features

* **Seamless ASIO Hot-Swapping**: On-the-fly switching between Standard output and ASIO exclusive mode. Maintains playback state and continues streaming without resetting the progress bar.
* **Portable Library Management**: An efficient, lightweight architecture for local track management, supporting effortless library path migration.
* **Minimalist & Stable**: Rebuilt on a clean C# / NAudio foundation, strictly avoiding unnecessary background overhead and resource waste.

---

## 🛠 Setup & Environment

### Prerequisites
* **OS**: Windows 10 / 11
* **IDE**: Visual Studio 2022 or any .NET-compatible IDE
* **Runtime**: .NET SDK (Latest version recommended)
* **Hardware**: An ASIO-compatible sound card/driver (or ASIO4ALL) is required for ASIO exclusive mode.

### Quick Start
1. Clone the repository: `git clone https://github.com/neunOrchid/Oscilla.git`
2. Open `Oscilla.sln` in Visual Studio.
3. Restore NuGet packages, then build and run the `Oscilla.UI` project.

---

## 📅 Roadmap

- [x] Core architecture built on NAudio (Standard/ASIO)
- [x] Driver-level seamless hot-swapping mechanism
- [x] Lightweight media library management
- [ ] More clean features to come...

---

## 📄 License

This project is licensed under the **Apache License 2.0**. See the [LICENSE](LICENSE) file for details.
