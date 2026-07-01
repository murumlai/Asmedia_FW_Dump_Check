using System;
namespace AsmTool
{
	public class AsmIOFactory
	{
		public static IAsmIO GetAsmIO() {
			if (OperatingSystem.IsWindows()) {
				return new WindowsAsmIO();
			}

			throw new NotSupportedException("ASMTool is supported on Windows only");
		}
	}
}
