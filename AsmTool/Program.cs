using System;

namespace AsmTool
{
	class Program
	{
		static void Main(string[] args) {
			if (args.Length != 1) {
				PrintUsage();
				return;
			}

			IAsmIO io = AsmIOFactory.GetAsmIO();

			Console.WriteLine("Unloading ASMEDIA Driver...");
			io.UnloadAsmIODriver();
			Console.WriteLine("Loading ASMEDIA Driver...");
			if(io.LoadAsmIODriver() != 1) {
				Console.Error.WriteLine("Failed to load ASMEDIA IO Driver");
				return;
			}

			AsmDevice dev = new AsmDevice(io);

			switch (args[0]) {
				case "read_fw_info":
					dev.PrintLiveFirmwareInfo(Console.Out);
					break;
				case "mem_read":
					dev.DumpMemory();
					break;
				case "flash_read":
					Console.WriteLine("Dumping firmware...");
					dev.DumpFirmware("dump.bin");
					break;
				default:
					PrintUsage();
					break;
			}

			io.UnloadAsmIODriver();
		}

		private static void PrintUsage() {
			Console.WriteLine("Usage: AsmTool.exe <command>");
			Console.WriteLine();
			Console.WriteLine("Commands:");
			Console.WriteLine("  read_fw_info  Read live firmware version info from controller flash without creating dump.bin.");
			Console.WriteLine("  mem_read      Dump controller memory to mem.bin.");
			Console.WriteLine("  flash_read    Dump controller SPI flash firmware ROM to dump.bin.");
		}
	}
}
