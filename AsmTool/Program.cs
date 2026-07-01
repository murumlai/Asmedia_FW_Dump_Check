#region License
/*
 * Copyright (C) 2019 Stefano Moioli <smxdev4@gmail.com>
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */
#endregion
﻿using System;

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

			Console.WriteLine("Unloading ASM Driver...");
			io.UnloadAsmIODriver();
			Console.WriteLine("Loading ASM Driver...");
			if(io.LoadAsmIODriver() != 1) {
				Console.Error.WriteLine("Failed to load ASM IO Driver");
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
