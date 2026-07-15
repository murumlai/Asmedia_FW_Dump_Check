using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsmTool
{
	public static class AsmFirmware
	{
		// Full firmware value is build date (6 chars) + marker (2 chars) + version (4 chars).
		private const int FirmwareValueLength = 12;
		private const int BuildDateLength = 6;
		private const int MarkerLength = 2;
		private const int VersionLength = FirmwareValueLength - BuildDateLength - MarkerLength;
		private const int MinimumFirmwareBuildYear = 2020;
		private const int FirmwareVersionMetadataOffset = 0xB9;

		private static bool IsBcd(byte value) {
			return ((value >> 4) <= 9) && ((value & 0x0F) <= 9);
		}

		private static int BcdToInt(byte value) {
			return ((value >> 4) * 10) + (value & 0x0F);
		}

		private static bool IsFirmwareMarker(byte value) {
			return value == 0x50 || value == 0x70;
		}

		private static bool TryGetBuildDate(byte yearByte, byte monthByte, byte dayByte, out DateTime buildDate) {
			buildDate = default;

			if (!IsBcd(yearByte) || !IsBcd(monthByte) || !IsBcd(dayByte)) {
				return false;
			}

			var year = 2000 + BcdToInt(yearByte);
			var month = BcdToInt(monthByte);
			var day = BcdToInt(dayByte);

			if (year < MinimumFirmwareBuildYear || year > DateTime.UtcNow.Year + 1) {
				return false;
			}

			try {
				buildDate = new DateTime(year, month, day);
			} catch {
				return false;
			}

			return true;
		}

		private static bool TryFormatBuildDate(byte yearByte, byte monthByte, byte dayByte, out string buildDate) {
			buildDate = string.Empty;

			if (!TryGetBuildDate(yearByte, monthByte, dayByte, out var date)) {
				return false;
			}

			buildDate = $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";
			return true;
		}

		private static int GetFormatPriority(string storageFormat) {
			return storageFormat switch {
				"binary BCD" => 0,
				"binary BCD little-endian" => 1,
				"ASCII" => 2,
				_ => 3,
			};
		}

		private static int GetOffsetPriority(int offset) {
			return offset == FirmwareVersionMetadataOffset ? 0 : 1;
		}

		private static bool IsAsciiDigit(byte value) {
			return value >= (byte)'0' && value <= (byte)'9';
		}

		private static bool IsAsciiHexDigit(byte value) {
			return IsAsciiDigit(value) ||
				(value >= (byte)'a' && value <= (byte)'f') ||
				(value >= (byte)'A' && value <= (byte)'F');
		}

		private static byte AsciiDigitsToBcd(byte tens, byte ones) {
			return (byte)(((tens - (byte)'0') << 4) | (ones - (byte)'0'));
		}

		public static List<FirmwareVersionInfo> FindFirmwareVersionCandidates(byte[] data) {
			var candidates = new List<FirmwareVersionInfo>();

			void AddCandidate(FirmwareVersionInfo candidate) {
				if (!candidates.Any(existing =>
					existing.Offset == candidate.Offset &&
					existing.RawValue == candidate.RawValue &&
					existing.StorageFormat == candidate.StorageFormat)) {
					candidates.Add(candidate);
				}
			}

			// Layout: build date (3 BCD bytes) + marker (1 BCD byte) + version (2 hex bytes).
			void TryAddBinaryCandidate(int offset) {
				if (offset < 0 || offset + 6 > data.Length) {
					return;
				}

				var yearByte = data[offset];
				var monthByte = data[offset + 1];
				var dayByte = data[offset + 2];
				var markerByte = data[offset + 3];
				var versionByte1 = data[offset + 4];
				var versionByte2 = data[offset + 5];

				if (!IsFirmwareMarker(markerByte)) {
					return;
				}

				if (!TryFormatBuildDate(yearByte, monthByte, dayByte, out var buildDate)) {
					return;
				}

				var version = $"{versionByte1:X2}{versionByte2:X2}";
				var raw = $"{yearByte:X2}{monthByte:X2}{dayByte:X2}{markerByte:X2}{version}";
				AddCandidate(new FirmwareVersionInfo(offset, raw, buildDate, $"{markerByte:X2}", version, "binary BCD"));
			}

			// Layout: version (2 hex bytes) + marker (1 BCD byte) + build date (day/month/year BCD bytes).
			void TryAddReverseBinaryCandidate(int offset) {
				if (offset < 0 || offset + 6 > data.Length) {
					return;
				}

				var versionByte2 = data[offset];
				var versionByte1 = data[offset + 1];
				var markerByte = data[offset + 2];
				var dayByte = data[offset + 3];
				var monthByte = data[offset + 4];
				var yearByte = data[offset + 5];

				if (!IsFirmwareMarker(markerByte)) {
					return;
				}

				if (!TryFormatBuildDate(yearByte, monthByte, dayByte, out var buildDate)) {
					return;
				}

				var version = $"{versionByte1:X2}{versionByte2:X2}";
				var raw = $"{yearByte:X2}{monthByte:X2}{dayByte:X2}{markerByte:X2}{version}";
				AddCandidate(new FirmwareVersionInfo(offset, raw, buildDate, $"{markerByte:X2}", version, "binary BCD little-endian"));
			}

			// Layout: build date (6 digits) + marker (2 digits) + version (4 hex digits).
			void TryAddAsciiCandidate(int offset) {
				if (offset < 0 || offset + FirmwareValueLength > data.Length) {
					return;
				}

				for (var i = 0; i < BuildDateLength + MarkerLength; i++) {
					if (!IsAsciiDigit(data[offset + i])) {
						return;
					}
				}

				for (var i = BuildDateLength + MarkerLength; i < FirmwareValueLength; i++) {
					if (!IsAsciiHexDigit(data[offset + i])) {
						return;
					}
				}

				var yearByte = AsciiDigitsToBcd(data[offset], data[offset + 1]);
				var monthByte = AsciiDigitsToBcd(data[offset + 2], data[offset + 3]);
				var dayByte = AsciiDigitsToBcd(data[offset + 4], data[offset + 5]);

				if (!TryFormatBuildDate(yearByte, monthByte, dayByte, out var buildDate)) {
					return;
				}

				var markerText = Encoding.ASCII.GetString(data, offset + BuildDateLength, MarkerLength);
				if (markerText != "50" && markerText != "70") {
					return;
				}

				var version = Encoding.ASCII.GetString(data, offset + BuildDateLength + MarkerLength, VersionLength);
				var raw = Encoding.ASCII.GetString(data, offset, FirmwareValueLength);
				AddCandidate(new FirmwareVersionInfo(offset, raw, buildDate, markerText, version, "ASCII"));
			}

			for (var offset = 0; offset <= data.Length - FirmwareValueLength; offset++) {
				TryAddAsciiCandidate(offset);
			}

			for (var offset = 0; offset <= data.Length - 6; offset++) {
				TryAddBinaryCandidate(offset);
				TryAddReverseBinaryCandidate(offset);
			}

			return candidates
				.OrderBy(candidate => GetOffsetPriority(candidate.Offset))
				.ThenBy(candidate => GetFormatPriority(candidate.StorageFormat))
				.ThenByDescending(candidate => candidate.BuildDate)
				.ThenBy(candidate => candidate.Offset)
				.ToList();
		}

		public readonly struct FirmwareVersionInfo
		{
			public int Offset { get; }
			public string RawValue { get; }
			public string BuildDate { get; }
			public string Marker { get; }
			public string Version { get; }
			public string StorageFormat { get; }

			public FirmwareVersionInfo(
				int offset,
				string rawValue,
				string buildDate,
				string marker,
				string version,
				string storageFormat) {
				Offset = offset;
				RawValue = rawValue;
				BuildDate = buildDate;
				Marker = marker;
				Version = version;
				StorageFormat = storageFormat;
			}
		}
	}
}
