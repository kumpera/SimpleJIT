//
// IndirectRegister.cs
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

using System;
using System.IO;

namespace SimpleJit {

public class IndirectRegister : ModRM {
	Register baseReg;
	Register indexReg;
	byte scale;
	int disp;
	bool force32;

	public IndirectRegister (Register baseReg, int disp, Register indexReg, byte scale, bool force32) {
		if (baseReg == null)
			throw new ArgumentNullException ("BaseReg must be non null");
		this.baseReg = baseReg;
		this.disp = disp;
		this.indexReg = indexReg;
		this.scale = scale;
		this.force32 = force32;
	}

	public IndirectRegister (Register baseReg) : this (baseReg, 0, null, SCALE_1, false) {
	}

	public override string ToString () {
		string res = "[" + baseReg.ToString ();
		if (indexReg != null)
			res += " + " + indexReg.ToString () + " * " + (1 << scale);
		if (disp != 0)
			res += " + " + disp;
		return res + "]";
	}

	public override void EncodeModRm (Stream buffer, byte constant) {
		if (disp == 0)
			EncodeIndirect (buffer, constant);
	}

	void EncodeIndirect (Stream buffer, byte constant) {
		if (indexReg != null)
			throw new ArgumentException ("Cant encode scaled indirect");
		if (baseReg == ESP) {
			buffer.WriteByte (CombineModRM (MOD_R32_PTR, baseReg.Index, constant));
			buffer.WriteByte (0x24);
		} else if (baseReg == EBP) {
			buffer.WriteByte (CombineModRM (MOD_R32_PTR_DISP8, baseReg.Index, constant));
			buffer.WriteByte (0x0);
		} else {
			buffer.WriteByte (CombineModRM (MOD_R32_PTR, baseReg.Index, constant));
		}
	}
}

}