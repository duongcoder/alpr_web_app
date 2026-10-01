================================================================================
  ALPR SDK (.NET FRAMEWORK 4.8 x64) - GÓI BÀN GIAO TR?M CÂN XE T?I
  Version: 1.0 Release
  Target: .NET Framework 4.8 / Windows x64 (WinForms / C#)
================================================================================

1. YÊU C?U B?T BU?C KHI C?U HÌNH TRONG VISUAL STUDIO:
------------------------------------------------------
Vào Project Properties c?a ph?n m?m cân -> Tab 'Build':
- Chuy?n 'Platform target' thành 'x64'
- B? CH?N m?c 'Prefer 32-bit'

2. CÁCH S? D?NG:
----------------
- Add Reference file: Bin\AlprSdk.Net48.dll
- Copy toàn b? file trong thu m?c Bin\ và Models\ vào thu m?c ch?y (.exe) c?a ph?n m?m cân.
- Xem chi ti?t t?i: integration_guide_net48.md

3. CH?Y TH? DEMO:
-----------------
Chuy?n vào thu m?c Demo và ch?y:
   dotnet run --project AlprSdk.Net48.Demo.csproj
