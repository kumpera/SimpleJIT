//
// ModRM.cs
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

public abstract class ModRM {
	public const byte MOD_R32_PTR = 0x0;
	public const byte MOD_R32_PTR_DISP8 = 0x1;
	public const byte MOD_R32_PTR_DISP32 = 0x2;
	public const byte MOD_R32 = 0x3;
	public const byte SIB_BYTE_IDX = 0x4;

	public const byte SCALE_1 = 0x0;
	public const byte SCALE_2 = 0x1;
	public const byte SCALE_4 = 0x2;
	public const byte SCALE_8 = 0x3;

	public void EncodeModRm (Stream buffer, Register reg) {
		EncodeModRm (buffer, reg.Index);
	}

	public abstract void EncodeModRm (Stream buffer, byte constant);


	public static byte CombineModRM (byte modRM, byte effAddr, byte reg) {
        return (byte) ((modRM << 6) | (reg << 3) | effAddr);
	}

	public static byte EncodeSib (byte base_reg, byte index, byte scale) {
        return (byte) ((scale << 6) | (index << 3) | base_reg);
	}
}

}
