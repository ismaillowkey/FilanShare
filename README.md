# FilanShare

> **Stealth Peer-to-Peer Local File Sharing Application for Windows and Linux**  
> *Built with .NET 8 / 10 and Avalonia UI*

---

## 🇬🇧 English Documentation

### Overview
**FilanShare** is a lightweight, cross-platform local network (LAN) file sharing solution designed for performance, security, and simplicity. Unlike typical local sharing apps that constantly broadcast UDP/mDNS discovery beacons, FilanShare operates in **Stealth Mode**—it never broadcasts its existence, preventing unauthorized network scanning while enabling lightning-fast, high-throughput peer-to-peer file transfers between Windows and Linux machines.

---

### ✨ Key Features

1. **🔒 100% Stealth Security**
   - No UDP broadcast, mDNS, or SSDP discovery beacons emitted.
   - Your device remains completely invisible to automated network scanners on public or office LANs.
   - Connections are made directly peer-to-peer using explicit IP address, Port, and user credentials.

2. **🌐 Multi-Interface & Dual-Mode Networking**
   - Listens on `0.0.0.0` (all network adapters: Wi-Fi, Ethernet, WSL, PLCSIM, and virtual adapters) in IPv4/IPv6 dual mode.
   - Intelligent interface dropdown prioritizing active physical adapters with explicit connection info for client devices.

3. **👥 User Account & Permission Management**
   - Fine-grained user control with independent checkboxes for **Read** (Download), **Write** (Upload), and **Delete**.
   - Default `admin` user is automatically provisioned with full permissions.
   - Additional custom users can be added, modified, or removed with custom passwords.

4. **📁 Versatile File & Folder Operations**
   - **Download:** Right-click context menu or toolbar button to download selected files or entire folder hierarchies, prompting for destination folder.
   - **Upload:** Drag-and-drop files or folders directly into the client explorer, or use the file picker dialog with multi-file support.
   - **Delete:** Remove files or folders on the server (permission-protected) with single and batch confirmation dialogs.

5. **🛡️ Automated Firewall Management**
   - Inspects Windows Firewall rules on startup and automatically creates rules for the active port.
   - Visual status badge (Open / Blocked) with a one-click **"Open"** button.

6. **💻 Connected Devices Activity Dashboard**
   - Real-time tracking of recently connected client IP addresses and hostnames.
   - Displays real-time activities (e.g. *Open root folder*, *Browse: /docs*, *Download: file.zip*, *Upload: setup.exe*).

7. **⚡ Background System Tray**
   - Option to minimize to the System Tray when clicking the window close button (`[X]`).
   - Server stays active and responsive in the background with quick tray restore or exit.

8. **🌐 Built-in Web Portal Fallback**
   - Devices without the desktop app (e.g. mobile phones, smart TVs) can browse and download shared files directly through standard web browsers via `http://<IP>:<Port>`.

9. **🌍 Instant Bilingual Localization**
   - Top-right language switch between **🇬🇧 EN (English)** and **🇮🇩 ID (Bahasa Indonesia)**.
   - All labels, dialogs, status messages, and logs adapt in real time.

---

### 🚀 Getting Started

#### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or [.NET 10.0 SDK](https://dotnet.microsoft.com/download) installed.

#### Run in Development
```bash
# Clone or navigate to the project directory
cd FileSender

# Run with dotnet CLI
dotnet run
```

#### Build Release
```bash
dotnet build -c Release
```

---

### 📦 Standalone & Installer Scripts

The project includes preconfigured root build scripts:

| Script | Platform | Output Description |
|---|---|---|
| `create_portable_winx86.bat` | Windows x86 / x64 | Self-contained single-file `.exe` portable executable + `.zip` package on Desktop. No .NET runtime required. |
| `create_installer_winx86.bat` | Windows x86 / x64 | Windows installer package with Desktop and Start Menu shortcut scripts. |
| `create_installer_linuxx86.bat` | Linux x86_64 | Standalone Linux binary with `install.sh` and `.desktop` launcher creation. |

---

### 📖 Usage Guide

#### 📡 Host Mode (Sharing Files)
1. Launch **FilanShare** and navigate to **📁 File > 📡 Host Mode (Share)**.
2. Select the folder you wish to share (defaults to `Desktop\File Sharing`).
3. Set your desired port (default `5050`) and configure user credentials in **⚙️ Settings**.
4. Click **🚀 START SHARING**.
5. Provide the displayed IP Address, Port, Username, and Password to client computers.

#### 💻 Client Mode (Browsing & Transferring)
1. Open **FilanShare** on another computer and go to **📁 File > 💻 Client Mode (Browse)**.
2. Enter the Host machine's IP, Port, Username, and Password.
3. Click **⚡ CONNECT**.
4. Browse files, double-click directories to open them, drag-and-drop files to upload, or right-click to download/delete.

---
---

## 🇮🇩 Dokumentasi Bahasa Indonesia

### Gambaran Umum
**FilanShare** adalah aplikasi transfer dan berbagi file peer-to-peer (P2P) lokal berkecepatan tinggi yang dirancang untuk performa, kemudahan penggunaan, dan privasi penuh. Berbeda dengan aplikasi sharing pada umumnya yang terus-menerus menyiarkan beacon UDP/mDNS di jaringan lokal, FilanShare bekerja dalam **Mode Stealth**—aplikasi tidak memancarkan sinyal penemuan otomatis sehingga aman dari pemindaian liar oleh perangkat lain di jaringan LAN publik maupun kantor.

---

### ✨ Fitur Unggulan

1. **🔒 Keamanan 100% Stealth**
   - Tidak memancarkan beacon UDP broadcast, mDNS, atau SSDP.
   - Perangkat Anda tidak akan terdeteksi oleh scanner otomatis di jaringan LAN publik atau kantor.
   - Koneksi dilakukan secara privat langsung antar-perangkat menggunakan IP Host, Port, dan akun pengguna.

2. **🌐 Multi-Interface & Jaringan Dual-Mode**
   - Server mendengarkan koneksi di `0.0.0.0` (seluruh adapter jaringan: Wi-Fi, Ethernet, WSL, PLCSIM, dan virtual adapter) menggunakan socket dual-mode IPv4/IPv6.
   - Dropdown cerdas memprioritaskan interface fisik aktif dan menyajikan informasi kredensial yang siap disalin ke klien.

3. **👥 Manajemen Akun Pengguna & Hak Akses**
   - Hak akses detail per pengguna: **Baca / Unduh (Read)**, **Tulis / Unggah (Write)**, dan **Hapus (Delete)**.
   - Akun default `admin` otomatis tersedia dengan seluruh hak akses tercentang.
   - Anda dapat menambah, mengubah, atau menghapus akun pengguna tambahan sesuai kebutuhan.

4. **📁 Operasi File & Folder Lengkap**
   - **Unduh (Download):** Klik kanan pada item atau gunakan tombol toolbar untuk mendownload file tunggal atau seluruh hierarki folder, dilengkapi pilihan folder tujuan penyimpanan.
   - **Unggah (Upload):** Cukup seret (drag-and-drop) file atau folder ke dalam aplikasi, atau pilih file menggunakan dialog penjelajah berkas.
   - **Hapus (Delete):** Hapus file atau folder dari server secara langsung dengan proteksi dialog konfirmasi.

5. **🛡️ Manajemen Firewall Otomatis**
   - Mendeteksi aturan Windows Firewall saat server berjalan dan otomatis membuka izin port yang aktif.
   - Dilengkapi lencana status visual (Terbuka / Tertutup) dan tombol **"Buka"** satu-klik jika diperlukan.

6. **💻 Riwayat Perangkat Terhubung (Connected Devices)**
   - Menampilkan daftar IP dan nama komputer yang baru saja mengakses server secara langsung.
   - Mencatat aktivitas spesifik secara real-time (contoh: *Buka folder utama*, *Jelajah: /dokumen*, *Unduh: arsip.zip*, *Unggah: aplikasi.exe*).

7. **⚡ Dukungan System Tray Latar Belakang**
   - Opsi untuk meminimalkan aplikasi ke System Tray saat tombol tutup (`[X]`) ditekan.
   - Server tetap aktif melayani transfer file di latar belakang tanpa mengganggu tampilan desktop Anda.

8. **🌐 Akses Cadangan via Web Browser**
   - Perangkat lain yang tidak memiliki aplikasi desktop (misal: ponsel pintar atau tablet) dapat mengunduh file secara langsung lewat browser di alamat `http://<IP>:<Port>`.

9. **🌍 Pilihan Bahasa Antarmuka Instan**
   - Tombol bendera di sudut kanan atas untuk beralih instan antara **🇮🇩 ID (Bahasa Indonesia)** dan **🇬🇧 EN (English)**.
   - Seluruh teks, dialog konfirmasi, status transfer, dan log aktivitas otomatis menyesuaikan bahasa aktif.

---

### 🚀 Cara Menjalankan

#### Prasyarat
- Menginstal [.NET 8.0 SDK](https://dotnet.microsoft.com/download) atau [.NET 10.0 SDK](https://dotnet.microsoft.com/download).

#### Menjalankan Mode Pengembangan (Dev)
```bash
# Masuk ke direktori proyek
cd FileSender

# Jalankan dengan CLI dotnet
dotnet run
```

#### Build Rilis
```bash
dotnet build -c Release
```

---

### 📦 Script Pembuatan Installer & Portable

Di folder root proyek telah disediakan script batch otomatis:

| Script | Target Sistem | Hasil Pembuatan |
|---|---|---|
| `create_portable_winx86.bat` | Windows x86 / x64 | File `.exe` portable single-file mandiri (tanpa perlu install .NET) + arsip `.zip` di Desktop. |
| `create_installer_winx86.bat` | Windows x86 / x64 | Paket installer Windows mandiri dengan script pembuat shortcut Desktop dan Start Menu. |
| `create_installer_linuxx86.bat` | Linux x86_64 | Binary standalone Linux lengkap dengan script `install.sh` untuk membuat shortcut menu aplikasi. |

---

### 📖 Panduan Penggunaan

#### 📡 Mode Host (Membagikan File)
1. Buka **FilanShare**, pilih menu **📁 File > 📡 Mode Host (Bagikan)**.
2. Tentukan folder yang ingin dibagikan (default: `Desktop\File Sharing`).
3. Sesuaikan Port (default `5050`) serta akun pengguna di tab **⚙️ Setting**.
4. Klik tombol **🚀 MULAI SHARING**.
5. Berikan alamat IP, Port, Username, dan Password yang tertera kepada pengguna di komputer klien.

#### 💻 Mode Klien (Menjelajahi & Transfer)
1. Buka **FilanShare** pada komputer lain di jaringan LAN yang sama, lalu pilih tab **💻 Mode Klien (Jelajahi)**.
2. Masukkan IP Host, Port, Username, dan Password.
3. Klik tombol **⚡ HUBUNGKAN**.
4. Jelajahi file pada server, klik ganda untuk membuka folder, drag & drop file/folder untuk mengunggah, atau klik kanan untuk mengunduh dan menghapus file.
