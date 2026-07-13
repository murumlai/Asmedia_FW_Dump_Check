using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsmTool
{
	public static class AsmFirmware
	{
		// Full firmware value is build date (6 chars) + marker (2 chars) + version.
		private const int MaxFirmwareValueLength = 13;
		private const int BuildDateLength = 6;
		private const int MarkerLength = 2;
		private const int MaxVersionLength = MaxFirmwareValueLength - BuildDateLength - MarkerLength;

		private static bool IsBcd(byte value) {
			return ((value >> 4) <= 9) && ((value & 0x0F) <= 9);
		}

		private static int BcdToInt(byte value) {
			return ((value >> 4) * 10) + (value & 0x0F);
		}

		private static bool TryFormatBuildDate(byte yearByte, byte monthByte, byte dayByte, out string buildDate) {
			buildDate = string.Empty;

			if (!IsBcd(yearByte) || !IsBcd(monthByte) || !IsBcd(dayByte)) {
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

			buildDate = $"{year:D4}-{month:D2}-{day:D2}";
			return true;
		}

		private static bool IsHexDigit(char c) {
			return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
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

			// Layout: build date (3 BCD bytes) + marker (1 byte, 0x50/0x70) + version (remaining BCD bytes, variable length).
			void TryAddBinaryCandidate(int offset) {
				if (offset < 0 || offset + 4 > data.Length) {
					return;
				}

				var span = data.Span;
				var yearByte = span[offset];
				var monthByte = span[offset + 1];
				var dayByte = span[offset + 2];
				var markerByte = span[offset + 3];

				if (markerByte != 0x50 && markerByte != 0x70) {
					return;
				}

				if (!TryFormatBuildDate(yearByte, monthByte, dayByte, out var buildDate)) {
					return;
				}

				// Consume the remaining version bytes, stopping at padding/non-BCD bytes.
				// The whole value is capped at MaxFirmwareValueLength chars (date 6 + marker 2 + version).
				var versionBuilder = new StringBuilder();
				for (var p = offset + 4; p < data.Length; p++) {
					var b = span[p];
					if (b == 0x00 || b == 0xFF || !IsBcd(b)) {
						break;
					}

					if (versionBuilder.Length + 2 > MaxVersionLength) {
						break;
					}

					versionBuilder.Append($"{b:X2}");
				}

				if (versionBuilder.Length == 0) {
					return;
				}

				var version = versionBuilder.ToString();
				var raw = $"{yearByte:X2}{monthByte:X2}{dayByte:X2}{markerByte:X2}{version}";
				AddCandidate(new FirmwareVersionInfo(offset, raw, buildDate, $"{markerByte:X2}", version, "binary BCD"));
			}

			// Layout: build date (6 digits) + marker (2 chars, "50"/"70") + version (remaining hex chars, variable length).
			void TryAddAsciiCandidate(int offset) {
				if (offset < 0 || offset + 8 > data.Length) {
					return;
				}

				var span = data.Span;

				for (var i = 0; i < 6; i++) {
					if (!char.IsDigit((char)span[offset + i])) {
						return;
					}
				}

				var yearByte = Convert.ToByte($"{(char)span[offset]}{(char)span[offset + 1]}", 16);
				var monthByte = Convert.ToByte($"{(char)span[offset + 2]}{(char)span[offset + 3]}", 16);
				var dayByte = Convert.ToByte($"{(char)span[offset + 4]}{(char)span[offset + 5]}", 16);

				var markerText = $"{(char)span[offset + 6]}{(char)span[offset + 7]}";
				if (markerText != "50" && markerText != "70") {
					return;
				}

				if (!TryFormatBuildDate(yearByte, monthByte, dayByte, out var buildDate)) {
					return;
				}

				// Consume the remaining version characters, stopping at the first non-hex char.
				// The whole value is capped at MaxFirmwareValueLength chars (date 6 + marker 2 + version).
				var versionBuilder = new StringBuilder();
				for (var p = offset + 8; p < data.Length; p++) {
					var c = (char)span[p];
					if (!IsHexDigit(c)) {
						break;
					}

					if (versionBuilder.Length + 1 > MaxVersionLength) {
						break;
					}

					versionBuilder.Append(c);
				}

				if (versionBuilder.Length == 0) {
					return;
				}

				var version = versionBuilder.ToString();
				var raw = $"{(char)span[offset]}{(char)span[offset + 1]}{(char)span[offset + 2]}{(char)span[offset + 3]}{(char)span[offset + 4]}{(char)span[offset + 5]}{markerText}{version}";
				AddCandidate(new FirmwareVersionInfo(offset, raw, buildDate, markerText, version, "ASCII"));
			}

			for (var offset = 0; offset <= data.Length - 4; offset++) {
				TryAddBinaryCandidate(offset);
			}

			for (var offset = 0; offset <= data.Length - 8; offset++) {
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
