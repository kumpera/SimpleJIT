using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;
using SimpleJit;

public delegate int NoArgsReturnInt ();
class Test : BuiltinRegisters {
	[DllImport ("libc", EntryPoint="mmap")]
	public static extern IntPtr mmap (IntPtr addr, IntPtr len, int prot, int flags, uint off_t);

	public static unsafe void Main () {
		int prot = 0x1 | 0x2 | 0x4; //PROT_READ | PROT_WRITE | PROT_EXEC
		int flags = 0x1000 | 0x0002; //MAP_ANON | MAP_PRIVATE
		var res = mmap (IntPtr.Zero, (IntPtr)4096, prot , flags, 0);
		var mem = new UnmanagedMemoryStream ((byte*)res, 4096, 4096, FileAccess.ReadWrite);
		var asm = new Assembler (mem);

		asm.Push (EBP);
		asm.Mov (EBP, ESP);
		asm.Mov (EAX, 10);
		asm.Leave ();
		asm.Ret ();

		NoArgsReturnInt dele = (NoArgsReturnInt)Marshal.GetDelegateForFunctionPointer (res, typeof (NoArgsReturnInt));

		Console.WriteLine (dele ());
	}
}
