# System Architecture & Desktop Packaging

This document outlines the deployment, installation architecture, and OS integration strategies for the desktop application suite.

---

## 🖥️ Windows Deployment Architecture (Inno Setup)

The Windows installer is engineered using **Inno Setup** to provide a native, zero-friction installation experience while enforcing modern 64-bit platform requirements and secure file permissions.

### 1. Platform Targeting & Architecture Constraints
To ensure compatibility with modern desktop hardware, the installer explicitly restricts execution to 64-bit environments:
* **Architectures Allowed:** `x64compatible` (Targets x64 systems and Windows 11 on Arm natively).
* **Installation Mode:** Runs in native 64-bit mode (`ArchitecturesInstallIn64BitMode=x64compatible`), placing binaries directly into the native 64-bit `Program Files` directory and interacting with the 64-bit registry hive.

### 2. State Persistence & App Data Isolation
The backend application requires a dedicated, persistent storage location for local databases, cryptographic keys, and user settings that survives application updates without requiring administrator elevation during runtime:
* **Common Application Data:** Configured to provision a secure directory under `{commonappdata}\[AppName]`.
* **Explicit Permissions:** Grants `users-full` permissions to the data directory during installation, ensuring that low-privilege user accounts can read and write local cache and configuration files seamlessly.
* **Lifecycle Management:** Implements automated cleanup rules (`UninstallDelete`) to cleanly purge shared application data and cache directories upon uninstallation.

### 3. Native OS Integration & File Associations
To elevate the desktop experience from a simple background API to a first-class citizen operating system application:
* **File Type Associations (`.myp`):** Automatically registers custom file extensions (`ChangesAssociations=yes`), allowing users to open project or data files directly with the application executable.
* **Shell Integration:** Dynamically generates desktop and start menu shortcuts with embedded application icons, respecting user task preferences during setup.

---

## 📊 Windows Deployment Topology Diagram

                +------------------------------------+
                |        Inno Setup Installer        |
                |   (x64 / Arm64 Native Targeting)   |
                +-----------------+-----------------+
                                |
            +---------------------+---------------------+
            |                                           |
            v                                           v
    +-----------+--------------------+          +-----------+--------------------+
    | Program Files Directory        |          | Common Application Data        |
    | - AppCore.exe                  |          | - {commonappdata}\AppCore      |
    | - wwwroot (Static Assets)      |          | - Local SQLite DB / Keys       |
    | - AppUpdater.exe               |          | - User Config Files            |
    +--------------------------------+          +--------------------------------+

---

## 🔒 Windows Security & Execution Resilience
* **Isolated Background Updates:** Works in tandem with the zero-downtime updater utility, ensuring that file-lock contention is managed cleanly outside the primary installation directory during version upgrades.
* **Modern UI Wizard:** Leverages Inno Setup's modern dynamic visual style for a polished, professional installation wizard experience.

## 🐧 Linux ARM64 Deployment & Debian Packaging

To support headless Linux environments and ARM64 single-board computers (such as Raspberry Pi hardware) without requiring a pre-installed .NET runtime, the application uses **self-contained ARM64 publishing**.

### 1. Self-Contained Publishing Pipeline
The build script compiles the backend Web API and its runtime dependencies directly into a standalone binary payload targeting `linux-arm64` (`--self-contained true`), guaranteeing execution consistency across diverse Linux distributions.

### 2. Lifecycle & System Integration Hooks (`postinst`)
Native Debian installation scripts automate system service registration and file permissions during package deployment:
```bash
#!/bin/bash
set -e

APP_NAME="portfolio-app"
INSTALL_DIR="/opt/$APP_NAME"
DATA_DIR="/var/lib/$APP_NAME"

# Ensure application data and logging directories exist with correct permissions
mkdir -p "$DATA_DIR/logs"
chmod 755 "$INSTALL_DIR"
chmod 700 "$DATA_DIR"

# Restore executable permissions for primary binaries and updaters
chmod +x "$INSTALL_DIR/AppCore"
chmod +x "$INSTALL_DIR/AppUpdater"
```

## 📊 Linux Deployment Pipeline Diagram

              +------------------------------------+
              |       .NET Source Code (C#)        |
              +-----------------+-----------------+
                                |
                                v
              +-----------------+-----------------+
              |   Self-Contained ARM64 Publish     |
              |  (`dotnet publish -r linux-arm64`) |
              +-----------------+-----------------+
                                |
                                v
              +-----------------+-----------------+
              | Debian Package Assembly (.deb)     |
              | (Control Metadata + Postinst Hooks)|
              +-----------------+-----------------+
                                |
                                v
              +-----------------+-----------------+
              | Target Linux Host / ARM64 Node    |
              | (`sudo dpkg -i portfolio-app.deb`)|
              +-----------------------------------+

---

## 🔒 Security & Sandboxing on Linux
* **Data Protection Key Ring:** Configures ASP.NET Core Data Protection to persist encryption keys securely to `/var/lib/[AppName]/keys`, ensuring session tokens survive application restarts on headless Linux hosts.
* **Non-Privileged Execution:** Designed to run under dedicated system user groups, minimizing attack surfaces on edge hardware.