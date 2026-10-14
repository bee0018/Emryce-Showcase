# .NET Cross-Platform Desktop & Web API Architecture Showcase

> **Portfolio Notice:** This public repository highlights system design patterns, architectural choices, and sample utility components from a commercial desktop application suite. Core business logic and proprietary workflows are maintained in a private upstream repository.

---

## 🛠️ Technology Stack
* **Backend Runtime:** .NET 10 / C# (Modular Web API hosted via Kestrel)
* **Frontend Integration:** React / TypeScript client communicating over local loopback (REST & SignalR)
* **Database & Persistence:** SQLite with automatic EF Core migration handling on startup
* **Packaging & Distribution:** Cross-platform installers targeting Windows (Inno Setup) and Linux (Debian packages with custom `systemd` / `chmod` hooks)

---

## 📂 Featured Sample Components

This repository contains two production-grade architectural samples from the core codebase:

1. **[ServiceCollectionExtensions.cs](./samples/ServiceCollectionExtensions.cs)** 
   * Demonstrates clean dependency injection organization, custom Kestrel local certificate binding, rate-limiting policies, and platform-aware DataProtection key persistence (handling Linux common data paths vs. Windows defaults).
2. **[AppUpdater.cs](./samples/AppUpdater.cs)**
   * A resilient zero-downtime auto-updater utility that handles process termination waiting, nested archive extraction (`.zip`), local XML configuration backup/restoration, file-lock retry contention logic, and Linux `chmod` permission restoration.

---

## 🏗️ Architectural Overview

            +-----------------------------------+
            |       Client UI (Desktop/Web)     |
            +-----------------+-----------------+
                                | HTTPS / REST & SignalR
                                v
            +-----------------+-----------------+
            |        Local .NET Web API         |
            |  (Kestrel + Rate Limiting + DI)   |
            +-----------------+-----------------+
                                |
        +-----------------------+-----------------------+
        |                                               |
        v                                               v
    +--------+----------------------+       +----------------+----------------+
    | Local Process / Git Engines   |       |  SQLite Database & Migrations   |
    +-------------------------------+       +---------------------------------+


## 📬 Contact

* **Developer:** Bryce Ellis
* **GitHub:** [github.com/bee0018/Emryce-Showcase](https://github.com/bee0018/Emryce-Showcase)