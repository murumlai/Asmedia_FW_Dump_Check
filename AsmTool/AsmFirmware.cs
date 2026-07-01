#region License
/*
 * Copyright (C) 2023 Stefano Moioli <smxdev4@gmail.com>
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */
#endregion
using Smx.SharpIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsmTool
{
	public enum AsmFirmwareChipType : byte {
		Asm2142 = 0x50,
		Asm3142 = 0x70
	}

	public class AsmFirmware : IDisposable
	{
		private readonly string filePath;
		private readonly MFile mf;
		private readonly SpanStream stream;

		private const string MAGIC_GEN1 = "2114A_RCFG";
		private const string MAGIC_GEN2 = "2214A_RCFG";

		private Span<byte> Span {
			get {
				return mf.Span.Memory.Span;
			}
		}

		public AsmFirmware(string firmwarePath) {
			filePath = firmwarePath;
			mf = MFile.Open(firmwarePath, FileMode.Open,
				FileAccess.Read | FileAccess.Write, FileShare.Read);
			stream = new SpanStream(mf);
		}

		private void UpdateChecksum() {
			Span[HeaderChecksumOffset] = ComputeHeaderChecksum();
			Span[BodyChecksumOffset] = ComputeBodyChecksum();
		}

		public void SetChipType(AsmFirmwareChipType type) {
			Span[0xBC] = (byte)type;
			UpdateChecksum();
		}

		private int GetSignatureType() {
			string magic = ReadStringSignature();
			switch(magic) {
				case MAGIC_GEN1: return 0;
				case MAGIC_GEN2: return 1;
				default:
					throw new InvalidDataException($"Unexpected magic \"{magic}\"");
			}
		}

		private byte[] GetFirmwareVersion() {
			var fwVer = stream.PerformAt(0xB9, () => {
				return stream.ReadBytes(6);
			});
			return fwVer;
		}

		private static bool IsBcd(byte value) {
			return ((value >> 4) <= 9) && ((value & 0x0F) <= 9);
		}

		private static int BcdToInt(byte value) {
			return ((value >> 4) * 10) + (value & 0x0F);
		}

		private static bool TryBuildFirmwareVersionInfo(
			int offset,
			byte yearByte,
			byte monthByte,
			byte dayByte,
			byte markerByte,
			byte versionHighByte,
			byte versionLowByte,
			string storageFormat,
			out FirmwareVersionInfo info
		) {
			info = default;

			if (!IsBcd(yearByte) ||
				!IsBcd(monthByte) ||
				!IsBcd(dayByte) ||
				!IsBcd(markerByte) ||
				!IsBcd(versionHighByte) ||
				!IsBcd(versionLowByte)) {
				return false;
			}

			if (markerByte != 0x50 && markerByte != 0x70) {
				return false;
			}

			var year = 2000 + BcdToInt(yearByte);
			var month = BcdToInt(monthByte);
			var day = BcdToInt(dayByte);

			try {
				_ = new DateTime(year, month, day);
			} catch {
				return false;
			}

			var rawValue = $"{yearByte:X2}{monthByte:X2}{dayByte:X2}{markerByte:X2}{versionHighByte:X2}{versionLowByte:X2}";
			var buildDate = $"{year:D4}-{month:D2}-{day:D2}";
			var marker = $"{markerByte:X2}";
			var version = $"{versionHighByte:X2}{versionLowByte:X2}";

			info = new FirmwareVersionInfo(
				offset,
				rawValue,
				buildDate,
				marker,
				version,
				storageFormat
			);

			return true;
		}

		private List<FirmwareVersionInfo> FindFirmwareVersionCandidates() {
			var candidates = new List<FirmwareVersionInfo>();

			void AddCandidate(FirmwareVersionInfo candidate) {
				if (!candidates.Any(existing =>
					existing.Offset == candidate.Offset &&
					existing.RawValue == candidate.RawValue &&
					existing.StorageFormat == candidate.StorageFormat)) {
					candidates.Add(candidate);
				}
			}

			void TryAddBinaryCandidate(int offset) {
				if (offset < 0 || offset + 6 > Span.Length) {
					return;
				}

				if (TryBuildFirmwareVersionInfo(
					offset,
					Span[offset],
					Span[offset + 1],
					Span[offset + 2],
					Span[offset + 3],
					Span[offset + 4],
					Span[offset + 5],
					"binary BCD",
					out var info)) {
					AddCandidate(info);
				}
			}

			void TryAddAsciiCandidate(int offset) {
				if (offset < 0 || offset + 12 > Span.Length) {
					return;
				}

				var raw = Encoding.ASCII.GetString(Span.Slice(offset, 12));
				if (!raw.All(char.IsDigit)) {
					return;
				}

				var yearByte = Convert.ToByte(raw.Substring(0, 2), 16);
				var monthByte = Convert.ToByte(raw.Substring(2, 2), 16);
				var dayByte = Convert.ToByte(raw.Substring(4, 2), 16);
				var markerByte = Convert.ToByte(raw.Substring(6, 2), 16);
				var versionHighByte = Convert.ToByte(raw.Substring(8, 2), 16);
				var versionLowByte = Convert.ToByte(raw.Substring(10, 2), 16);

				if (TryBuildFirmwareVersionInfo(
					offset,
					yearByte,
					monthByte,
					dayByte,
					markerByte,
					versionHighByte,
					versionLowByte,
					"ASCII",
					out var info)) {
					AddCandidate(info);
				}
			}

			// Keep the old expected location first, but no longer depend on it.
			TryAddBinaryCandidate(0xB9);
			TryAddAsciiCandidate(0xB9);

			for (var offset = 0; offset <= Span.Length - 6; offset++) {
				TryAddBinaryCandidate(offset);
			}

			for (var offset = 0; offset <= Span.Length - 12; offset++) {
				TryAddAsciiCandidate(offset);
			}

			return candidates;
		}

		private FirmwareVersionInfo? GetBestFirmwareVersionInfo() {
			var candidates = FindFirmwareVersionCandidates();

			foreach (var candidate in candidates) {
				if (candidate.Offset == 0xB9) {
					return candidate;
				}
			}

			return candidates.FirstOrDefault();
		}

		private string GetFirmwareVersionString() {
			var versionInfo = GetBestFirmwareVersionInfo();
			if (versionInfo.HasValue) {
				return versionInfo.Value.RawValue;
			}

			var fwVer = GetFirmwareVersion();
			return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}{4:X2}{5:X2}",
				fwVer[0],
				fwVer[1],
				fwVer[2],
				fwVer[3],
				fwVer[4],
				fwVer[5]
			);
		}

		private string GetFirmwareVersionDescription() {
			var versionInfo = GetBestFirmwareVersionInfo();
			if (versionInfo.HasValue) {
				var info = versionInfo.Value;
				return $"{info.BuildDate}, marker: {info.Marker}, version: {info.Version}, offset: 0x{info.Offset:X}, format: {info.StorageFormat}";
			}

			var fwVer = GetFirmwareVersion();
			return string.Format(
				"20{0:X2}-{1:X2}-{2:X2}, marker: {3:X2}, version: {4:X2}{5:X2}, offset: 0xB9, format: fixed binary BCD fallback",
				fwVer[0],
				fwVer[1],
				fwVer[2],
				fwVer[3],
				fwVer[4],
				fwVer[5]
			);
		}

		private string GetFirmwareName() {
			var fwChipName = stream.PerformAt(0xB9 + 7, () => {
				return stream.ReadCString(Encoding.ASCII);
			});
			return fwChipName;
		}

		private string ReadFooterSignature() {
			var offset = HeaderSize + BodySize + 9;
			return stream.PerformAt(offset, () => {
				return stream.ReadString(8, Encoding.ASCII);
			});
		}

		private string ReadStringSignature() {
			return stream.PerformAt(6, () => { 
				return stream.ReadString(10, Encoding.ASCII);
			});
		}

		private ushort HeaderSize {
			get {
				return (ushort)((ushort)0u
					| (ushort)(Span[4] << 0)
					| (ushort)(Span[5] << 8)
				);
			}
		}

		private uint BodySize {
			get {
				return (0u
					| (uint)(Span[HeaderSize + 5] << 0)
					| (uint)(Span[HeaderSize + 6] << 8)
					| (uint)(Span[HeaderSize + 7] << 16)
				);
			}
		}

		private int HeaderChecksumOffset => HeaderSize;
		private int BodyChecksumOffset => (int)(HeaderSize + BodyStartOffset + BodySize + 8);

		
		private byte HeaderChecksum => Span[HeaderChecksumOffset];
		private byte BodyChecksum => Span[BodyChecksumOffset];

		private int BodyStartOffset {
			get {
				if (ReadStringSignature() == MAGIC_GEN1) return 7;
				return 9;
			}
		}

		private byte ComputeBodyChecksum() {
			var body_start_offset = BodyStartOffset;

			byte p0 = 0;
			byte p1 = 0;
			int i = 0;

			var body_start = HeaderSize + body_start_offset;
			if (BodySize >= 2) {
				for (i = 0; i < BodySize; i += 2) {
					p0 += Span[body_start + i];
					p1 += Span[body_start + i + 1];
				}
			}

			byte p2;
			if (i >= BodySize) {
				p2 = 0;
			} else {
				p2 = Span[body_start + i];
			}

			byte checksum = 0;
			checksum += p0;
			checksum += p1;
			checksum += p2;
			return checksum;
		}

		private byte ComputeHeaderChecksum() {
			byte p0 = 0;
			byte p1 = 0;
			int i = 0;
			if (HeaderSize >= 2) {
				for (i = 0; i < HeaderSize; i += 2) {
					p0 += Span[i];
					p1 += Span[i + 1];
				}
			}

			byte p2;
			if (i >= HeaderSize) {
				p2 = 0;
			} else {
				p2 = Span[i];
			}

			byte checksum = 0;
			checksum += p0;
			checksum += p1;
			checksum += p2;
			return checksum;

		}

		public void PrintInfo(AsmDevice dev, TextWriter os) {
			os.WriteLine("==== File Info ====");
			os.WriteLine($"File: {filePath}");

			var compHeaderChecksum = ComputeHeaderChecksum();
			var compBodyChecksum = ComputeBodyChecksum();

			os.WriteLine($"File Checksum [header]: 0x{HeaderChecksum:X2} (expected: 0x{compHeaderChecksum:X2})");
			os.WriteLine($"File Checksum   [body]: 0x{BodyChecksum:X2} (expected: 0x{compBodyChecksum:X2})");

			if (HeaderChecksum != compHeaderChecksum || BodyChecksum != compBodyChecksum) {
				os.WriteLine("!! WARNING: Checksum Mismatch");
			} else {
				os.WriteLine("Checksum OK");
			}

			os.WriteLine($"Signature: {ReadStringSignature()}");
			os.WriteLine($"FW Version: {GetFirmwareVersionString()}");
			os.WriteLine($"FW Version Info: {GetFirmwareVersionDescription()}");
			os.WriteLine($"FW Name: {GetFirmwareName()}");
			os.WriteLine($"Footer: {ReadFooterSignature()}");

			var versionCandidates = FindFirmwareVersionCandidates();
			if (versionCandidates.Count > 0) {
				os.WriteLine("FW Version Candidates:");
				foreach (var candidate in versionCandidates.Take(10)) {
					os.WriteLine(
						$"  Offset 0x{candidate.Offset:X6}: {candidate.RawValue} " +
						$"({candidate.BuildDate}, marker: {candidate.Marker}, version: {candidate.Version}, format: {candidate.StorageFormat})"
					);
				}
			} else {
				os.WriteLine("FW Version Candidates: none found");
			}			

			os.WriteLine("==== Actual Chip Info ====");

			var chipRev0 = dev.ReadMemory(0x150B2)?[0];
			var chipRev1 = dev.ReadMemory(0xF38C)?[0];

			if (chipRev0 != null) {
				os.WriteLine($"Chip Rev0: 0x{chipRev0:X2}");
			}
			if (chipRev1 != null) {
				os.WriteLine($"Chip Rev1: 0x{chipRev1:X2}");
			}
		}

		public void Dispose() {
			mf.Dispose();
		}

		private readonly record struct FirmwareVersionInfo(
			int Offset,
			string RawValue,
			string BuildDate,
			string Marker,
			string Version,
			string StorageFormat
		);
	}
}
