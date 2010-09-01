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

public class ScaledRegister {
	Register reg;
	byte scale;

	public ScaledRegister (Register reg, byte scale) {
		this.reg = reg;
		switch (scale) {
		case 1:
			this.scale = 0;
			break;
		case 2:
			this.scale = 1;
			break;
		case 4:
			this.scale = 2;
			break;
		case 8:
			this.scale = 3;
			break;
		default:
			throw new ArgumentException ("Invalid scale value");
		}
	}

	public Register Register {
		get { return reg; }
	}

	public byte Scale {
		get { return scale; }
	}
}

public class IndirectRegister : ModRM {
	Register baseReg;
	Register indexReg;
	byte scale;
	int disp;
	bool force32;

	public IndirectRegister (Register baseReg, int disp, Register indexReg, byte scale, bool force32) {
		if (baseReg == null)
			throw new ArgumentNullException ("BaseReg must be non null");
		if (indexReg == ESP)
			throw new ArgumentException ("IndexReg cannot be ESP");
		this.baseReg = baseReg;
		this.disp = disp;
		this.indexReg = indexReg;
		this.scale = scale;
		this.force32 = force32;
	}

	public IndirectRegister (Register baseReg) : this (baseReg, 0, null, SCALE_1, false) {
	}

	public IndirectRegister (Register baseReg, int disp) : this (baseReg, disp, null, SCALE_1, false) {
	}

	public IndirectRegister (Register baseReg, int disp, Register indexReg, byte scale) : this (baseReg, disp, indexReg, scale, false) {
	}

	public IndirectRegister (Register baseReg, ScaledRegister scale) : this (baseReg, 0, scale.Register, scale.Scale, false) {
	}

	public override string ToString () {
		string res = "[" + baseReg.ToString ();
		if (indexReg != null)
			res += " + " + indexReg.ToString () + " * " + (1 << scale);
		if (disp != 0)
			res += " + " + disp;
		return res + "]";
	}

    static bool IsImm8 (int imm) {
		return imm >= -128 && imm <= 127;
	}

	public override void EncodeModRm (Stream buffer, byte constant) {
		if (disp == 0)
			EncodeIndirect (buffer, constant);
		else if (IsImm8 (disp) && !force32)
			EncodeDisp8 (buffer, constant);
		else
			EncodeDisp32 (buffer, constant);
	}

	void EncodeDisp8 (Stream buffer, byte constant) {
		if (indexReg != null) {
			buffer.WriteByte (CombineModRM (MOD_R32_PTR_DISP8, 0x04, constant));
			buffer.WriteByte (CombineSib (baseReg.Index, indexReg.Index, scale));
		} else {
			buffer.WriteByte (CombineModRM (MOD_R32_PTR_DISP8, baseReg.Index, constant));
			if (baseReg == ESP)
				buffer.WriteByte (0x24);
		}
		buffer.WriteByte ((byte)disp);
	}

	void EncodeDisp32 (Stream buffer, byte constant) {
		if (indexReg != null) {
			buffer.WriteByte (CombineModRM (MOD_R32_PTR_DISP32, 0x04, constant));
			buffer.WriteByte (CombineSib (baseReg.Index, indexReg.Index, scale));
		} else {
			buffer.WriteByte (CombineModRM (MOD_R32_PTR_DISP32, baseReg.Index, constant));
			if (baseReg == ESP)
				buffer.WriteByte (0x24);
		}
		buffer.WriteInt (disp);
	}

	void EncodeIndirect (Stream buffer, byte constant) {
		if (indexReg != null) {
			if (baseReg == EBP) {
				buffer.WriteByte (CombineModRM (MOD_R32_PTR_DISP8, 0x04, constant));
				buffer.WriteByte (CombineSib (baseReg.Index, indexReg.Index, scale));
				buffer.WriteByte (0x0);
			} else {
				buffer.WriteByte (CombineModRM (MOD_R32_PTR, 0x04, constant));
				buffer.WriteByte (CombineSib (baseReg.Index, indexReg.Index, scale));
			}			
		} else {
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

	public static IndirectRegister operator +(IndirectRegister reg, int displacement) {
		return new IndirectRegister (reg.baseReg, reg.disp + displacement, reg.indexReg, reg.scale);
	}


}

}