# Asmedia FW Dump Check

ASMTool is a minimal Windows-only .NET 8 command-line utility for PCI-based ASMedia USB controllers. It supports only three read operations: reading live firmware version information, dumping controller flash, and dumping controller memory.

This fork focuses on reading firmware information directly from ASMedia flash data without hardcoding firmware values. Fixed offsets, signatures, and format rules may be used, but firmware version/date/marker values are read from device data.

Original upstream project: https://github.com/smx-smx/ASMTool

## Current capabilities

- Detects supported ASMedia PCI devices by product ID:
  - `0x2142` / ASM2142
  - `0x3142` / ASM3142
- Supports only these switches:
  - `read_fw_info`
  - `mem_read`
  - `flash_read`
- Dumps the controller flash ROM to `dump.bin`.
- Dumps controller memory to `mem.bin`.
- Reads MPTOOL-style firmware version candidates directly from controller flash data.

Removed features from the original ASMTool include firmware file inspection, firmware patching, Linux support, and unused memory-map/write helpers.

## Firmware version parsing

ASMedia MPTOOL-style firmware versions are parsed as a 12-digit value:

```text
YYMMDDMMVVVV
```

Example:

```text
210225700240
```

Meaning:

```text
21 02 25 70 02 40
│  │  │  │  └──── firmware version: 0240
│  │  │  └─────── firmware generation/update marker: 70
└──┴──┴────────── build date: 2021-02-25
```

The parser scans flash data for valid binary-BCD and ASCII version candidates. It reports the selected version, its offset, and additional candidates found in the data. Firmware values are not hardcoded.

## Requirements

### Build requirements

- .NET 8 SDK
- Visual Studio 2026 or another .NET 8-compatible build environment

The Windows build targets `x86` because the native ASMedia I/O DLL uses 32-bit interop.

### Runtime requirements

The tool requires ASMedia I/O driver components at runtime:

- `asmiodll.dll`
- ASMedia I/O driver files such as `AsmIo.sys` or `AsmIo64.sys`, depending on the environment

Place the required native files next to the built `AsmTool.exe`.

Administrator privileges may be required because the tool loads a driver and accesses PCI configuration/device registers.

## Build

From the repository root:

```powershell
dotnet build AsmTool.sln
```

The Windows output is produced as an x86 executable because of the project configuration.

## Usage

Run commands from the directory containing the built executable and required native I/O files.

```powershell
AsmTool.exe <read_fw_info|mem_read|flash_read>
```

Running without a switch or with an unknown switch prints usage.

### Read live firmware info without writing a dump

```powershell
AsmTool.exe read_fw_info
```

This reads the controller flash into memory, scans it for MPTOOL-style firmware version candidates, and prints the detected firmware version information without creating `dump.bin`.

Example output:

```text
==== Live Firmware Info ====
FW Version: 210225700240
FW Version Info: 2021-02-25, marker: 70, version: 0240, offset: 0xC9, format: binary BCD
FW Version Candidates:
  Offset 0x0000C9: 210225700240 (2021-02-25, marker: 70, version: 0240, format: binary BCD)
```

### Dump flash firmware

```powershell
AsmTool.exe flash_read
```

This reads the controller flash in 8-byte chunks and writes a 128 KiB dump to:

```text
dump.bin
```

### Dump mapped memory

```powershell
AsmTool.exe mem_read
```

This writes a memory dump to:

```text
mem.bin
```

## Important notes

- Firmware dump files such as `dump.bin` and `mem.bin` are ignored by Git through `.gitignore`.
- Use this tool only with hardware and firmware you are authorized to inspect.
- Low-level PCI/device access can hang hardware or the operating system if used incorrectly.

## Security considerations

ASMedia controller firmware interfaces can potentially write code to the device. A PCIe device with malicious firmware may be able to perform unsafe DMA or other privileged operations. Treat firmware images and flashing/debug tooling as security-sensitive.

## License

The original source files include MPL-2.0 license headers. See the file headers for licensing details.
