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
    
	internal Register (byte idx) {
		this.idx = idx;
	}

	public byte Index { get { return idx; } }

    public override void EncodeModRm (Stream buffer, byte constant) {
        buffer.WriteByte (CombineModRM (MOD_R32, idx, constant));
    }

	public override string ToString () {
		return names [idx];
	}

	public static IndirectRegister operator !(Register reg) {
		return new IndirectRegister (reg);
	}

	public static IndirectRegister operator +(Register reg, int displacement) {
		return new IndirectRegister (reg, displacement);
	}

	public static IndirectRegister operator -(Register reg, int displacement) {
		return new IndirectRegister (reg, -displacement);
	}

	public static IndirectRegister operator +(Register baseReg, Register indexReg) {
		return new IndirectRegister (baseReg, 0, indexReg, SCALE_1);
	}

}

}
