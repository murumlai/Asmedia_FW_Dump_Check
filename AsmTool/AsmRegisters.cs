namespace AsmTool
{
	/// <summary>
	/// byte0 of internal register
	/// </summary>
	public enum ASMIOCommand : uint {
		Read = 0x40
	}

	public enum ASMIOFunction : uint {
		Memory = 0x4,
		Flash = 0x10
	}
}