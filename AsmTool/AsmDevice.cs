using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AsmTool
{
	public class AsmDevice {
		const uint PID_2142 = 0x2142;
		const uint PID_3142 = 0x3142;

		const uint FIRMWARE_SIZE = 131072; //128k ROM

		private readonly Prober prb;
		private readonly PCIAddress pcidev;

		private readonly IAsmIO io;

		public AsmDevice(IAsmIO io) {
			this.io = io;
			this.prb = new Prober(io);

			Console.WriteLine("Scanning for ASMedia ICs...");
			if (!prb.FindByProduct(PID_2142, out pcidev) && !prb.FindByProduct(PID_3142, out pcidev))
				throw new Exception($"No ASMedia device detected!");

			Console.WriteLine("Found ASMedia IC!");
		}

		private static byte[] BuildAsmCommand(
			ASMIOCommand mode, ASMIOFunction function, byte size
		) {
			return new byte[] {
				(byte)mode, (byte)function, size
			};
		}

		private UInt32 WriteRegister(byte[] reg, uint data, uint data2 = 0, uint data3 = 0) {
			return io.WriteCmdALL(pcidev.Bus, pcidev.Device, pcidev.Function,
				reg[0], reg[1], reg[2],
				data, data2, data3
			);
		}

		private unsafe byte[]? ReadMemory(uint address) {
			byte wordSize = 4;
			var reg = BuildAsmCommand(ASMIOCommand.Read, ASMIOFunction.Memory, wordSize);
			if (WriteRegister(reg, address) < 0) {
				Console.WriteLine("WriteRegister failed!");
				return null;
			}

			if (!ReadPacket(wordSize, out byte[]? ack) || ack == null) {
				Console.WriteLine("Failed to read ack!");
				return null;
			}

			byte[] word = new byte[wordSize];
			if (!ReadPacketSmall(wordSize, word)) {
				Console.WriteLine("Failed to read data!");
				return null;
			}
			return word;
		}

		private unsafe bool ReadPacketSmall(int wordSize, byte[] data) {
			if (io.Wait_Read_Ready(pcidev.Bus, pcidev.Device, pcidev.Function) < 0) {
				Console.WriteLine("Wait_Read_Ready failed!");
				return false;
			}

			for (int i = 0; i < wordSize; i++) {
				byte offset = (byte)(0xF0 + i);
				data[i] = io.PCI_Read_BYTE(pcidev.Bus, pcidev.Device, pcidev.Function, offset);
			}
			// signal read end
			io.PCI_Write_BYTE(pcidev.Bus, pcidev.Device, pcidev.Function, 0xE0, 1);

			return true;
		}

		private unsafe bool ReadPacket(uint wordSize, out byte[]? data) {
			data = null;

			if (io.Wait_Read_Ready(pcidev.Bus, pcidev.Device, pcidev.Function) < 0) {
				Console.WriteLine("Wait_Read_Ready failed!");
				return false;
			}

			data = new byte[0x2C];
			fixed (byte* ptr = data) {
				io.ReadCMD(pcidev.Bus, pcidev.Device, pcidev.Function, new IntPtr(ptr));
			}
			return true;
		}

		private static unsafe T? ReadStructure<T>(byte[] data) {
			fixed (byte* ptr = data) {
				return Marshal.PtrToStructure<T>(new IntPtr(ptr));
			}
		}

		public unsafe bool DumpFirmware(string filename) {
			uint num_reads = FIRMWARE_SIZE / 8;

			BinaryWriter bw = new BinaryWriter(new FileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite));
			for (uint i=0; i<num_reads; i++) {
				if (!SPIReadQword(i * 8, out ulong qword)) {
					Console.WriteLine($"Invalid SPI Read at offset {i * 8}");
					return false;
				}
				//Console.WriteLine($"QW: {qword:X16}");
				bw.Write(qword);
				//break;
				//Thread.Sleep(1000);
			}

			bw.Close();
			return true;
		}

		private byte[]? ReadFirmwareBytes() {
			uint num_reads = FIRMWARE_SIZE / 8;
			var firmware = new byte[(int)FIRMWARE_SIZE];

			for (uint i = 0; i < num_reads; i++) {
				var offset = i * 8;
				if (!SPIReadQword(offset, out ulong qword)) {
					Console.WriteLine($"Invalid SPI Read at offset {offset}");
					return null;
				}

				BitConverter.TryWriteBytes(firmware.AsSpan((int)offset, 8), qword);
			}

			return firmware;
		}

		public bool PrintLiveFirmwareInfo(TextWriter os) {
			var firmware = ReadFirmwareBytes();
			if (firmware == null) {
				return false;
			}

			var versionCandidates = AsmFirmware.FindFirmwareVersionCandidates(firmware);
			os.WriteLine("==== Reading Firmware Info ====");
			if (versionCandidates.Count > 0) {
				var selected = versionCandidates[0];
				os.WriteLine($"FW Version: {selected.RawValue}");
				os.WriteLine($"FW Version Info: {selected.BuildDate}, marker: {selected.Marker}, version: {selected.Version}, offset: 0x{selected.Offset:X}, format: {selected.StorageFormat}");
				//os.WriteLine("FW Version Candidates:");
				//foreach (var candidate in versionCandidates.Take(10)) {
				//	os.WriteLine(
				//		$"  Offset 0x{candidate.Offset:X6}: {candidate.RawValue} " +
				//		$"({candidate.BuildDate}, marker: {candidate.Marker}, version: {candidate.Version}, format: {candidate.StorageFormat})"
				//	);
				//}
			} else {
				os.WriteLine("FW Version: not found");
				os.WriteLine("FW Version Candidates: none found");
			}

			return true;
		}

		private bool SPIReadQword(uint offset, out ulong qword) {
			qword = 0;

			byte wordSize = 0x8;
			var SPIReg = BuildAsmCommand(ASMIOCommand.Read, ASMIOFunction.Flash, wordSize);
			if (WriteRegister(SPIReg, offset) < 0) {
				Console.WriteLine("WriteRegister failed!");
				return false;
			}

			if(!ReadPacket(wordSize, out byte[]? ack) || ack == null) {
				Console.WriteLine("Failed to read ack!");
				return false;
			}

			if(!ReadPacket(wordSize, out byte[]? reply) || reply == null) {
				Console.WriteLine("Failed to read reply!");
				return false;
			}

			AsmIOPacket pkt = ReadStructure<AsmIOPacket>(reply);
			
			// assemble qword in little endian order due to BinaryWriter being LE only
			qword = ((ulong)(pkt.Data2) << 32) | pkt.Data1;

			return true;
		}

		public void DumpMemory() {
			var MEM_SIZE = 128*1024 ;
			using var fh = new FileStream("mem.bin",
				FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
			fh.SetLength(0);

			var wordSize = 4;
			for (int i = 0; i < MEM_SIZE; i+=wordSize) {
				var data = ReadMemory((uint)i);
				if (data != null) {
					Console.WriteLine("writing");
					fh.Write(data);
				} else {
					break;
				}
			}
		}
	}
}
