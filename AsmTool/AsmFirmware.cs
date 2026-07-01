using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsmTool
{
	public static class AsmFirmware
	{
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

			info = new FirmwareVersionInfo(
				offset,
				$"{yearByte:X2}{monthByte:X2}{dayByte:X2}{markerByte:X2}{versionHighByte:X2}{versionLowByte:X2}",
				$"{year:D4}-{month:D2}-{day:D2}",
				$"{markerByte:X2}",
				$"{versionHighByte:X2}{versionLowByte:X2}",
				storageFormat
			);

			return true;
		}

		public static List<FirmwareVersionInfo> FindFirmwareVersionCandidates(ReadOnlyMemory<byte> data) {
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
				if (offset < 0 || offset + 6 > data.Length) {
					return;
				}

				var span = data.Span;
				if (TryBuildFirmwareVersionInfo(
					offset,
					span[offset],
					span[offset + 1],
					span[offset + 2],
					span[offset + 3],
					span[offset + 4],
					span[offset + 5],
					"binary BCD",
					out var info)) {
					AddCandidate(info);
				}
			}

			void TryAddAsciiCandidate(int offset) {
				if (offset < 0 || offset + 12 > data.Length) {
					return;
				}

				var raw = Encoding.ASCII.GetString(data.Span.Slice(offset, 12));
				if (!raw.All(char.IsDigit)) {
					return;
				}

				if (TryBuildFirmwareVersionInfo(
					offset,
					Convert.ToByte(raw.Substring(0, 2), 16),
					Convert.ToByte(raw.Substring(2, 2), 16),
					Convert.ToByte(raw.Substring(4, 2), 16),
					Convert.ToByte(raw.Substring(6, 2), 16),
					Convert.ToByte(raw.Substring(8, 2), 16),
					Convert.ToByte(raw.Substring(10, 2), 16),
					"ASCII",
					out var info)) {
					AddCandidate(info);
				}
			}

			for (var offset = 0; offset <= data.Length - 6; offset++) {
				TryAddBinaryCandidate(offset);
			}

			for (var offset = 0; offset <= data.Length - 12; offset++) {
				TryAddAsciiCandidate(offset);
			}

			return candidates;
		}

		public readonly record struct FirmwareVersionInfo(
			int Offset,
			string RawValue,
			string BuildDate,
			string Marker,
			string Version,
			string StorageFormat
		);
	}
}
