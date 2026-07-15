using System;
namespace AsmTool
{
	public class AsmIOFactory
	{
		public static IAsmIO GetAsmIO() {
			if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
				return new WindowsAsmIO();
			}

			throw new NotSupportedException("ASMTool is supported on Windows only");
		}
	}
}
