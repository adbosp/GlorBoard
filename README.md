# GlorBoard

GlorBoard là bộ gõ tiếng Việt Telex và VNI cho Windows, gồm bộ gõ và khay hệ thống viết bằng C++ cùng bảng điều khiển WPF.

## Tải và sử dụng

Tải `GlorBoard-1.2.4-win-x64.zip` từ [GitHub Releases](https://github.com/adbosp/GlorBoard/releases/latest). Giải nén toàn bộ gói rồi mở `GlorBoard.exe`. Giữ `GlorBoard.Panel.exe` cùng thư mục để mở bảng điều khiển.

## Build trên Windows

Cài .NET 10 SDK và Zig 0.15.2. Khi `zig.exe` có trong PATH, chạy từ thư mục gốc của dự án bằng PowerShell.

```powershell
.\native\build.ps1
```

Script tạo hai tệp thực thi trong `dist`. Có thể dùng tham số `-Zig` để chỉ định đường dẫn đến Zig, hoặc `-Output` để chọn thư mục đầu ra.

Bộ icon VI/TE nền trong suốt có trong thư mục `dist/icons` ở các kích thước 512, 256 và 128 px.
