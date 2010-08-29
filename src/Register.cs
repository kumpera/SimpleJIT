//
// Register.cs
//
// Author:
//   Rodrigo Kumpera  <kumpera@gmail.com>
//
//
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.IO;

namespace SimpleJit {


public class Register : ModRM {
	byte idx;

	static readonly string[] names = { "eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi" }; 

	public static readonly Register EAX = new Register (0);
	public static readonly Register ECX = new Register (1);
	public static readonly Register EDX = new Register (2);
	public static readonly Register EBX = new Register (3);
	public static readonly Register ESP = new Register (4);
	public static readonly Register EBP = new Register (5);
	public static readonly Register ESI = new Register (6);
	public static readonly Register EDI = new Register (7);
    
	Register (byte idx) {
		this.idx = idx;
	}

	public byte Index { get { return idx; } }

	public IndirectRegister Indirect {
		get { return new IndirectRegister (this); }
	}

    public override void EncodeModRm (Stream buffer, byte constant) {
        buffer.WriteByte (CombineModRM (MOD_R32, idx, constant));
    }

	public override string ToString () {
		return names [idx];
	}

}

}
