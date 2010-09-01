//
// Assembler.cs
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

public class Assembler {
	Stream buffer;

	public Assembler (Stream buffer) {
		this.buffer = buffer;
	}

	/* push r/m32*/
	public void Push (ModRM reg) {
		if (reg is Register) {
			buffer.WriteByte ((byte)(0x50 + ((Register) reg).Index));
		} else {
			buffer.WriteByte (0xFF);
			reg.EncodeModRm (buffer, (byte) 0x6);
		}
	}

	/* mov r32, r/m32 */
	public void Mov (Register dest, ModRM source) {
		buffer.WriteByte (0x8B);
		source.EncodeModRm (buffer, dest);
	}

	/* mov r/m32, r32
	   Note: for r32, r32 we favor the 0x8B encoding, so it's not possible to do it with this opcode.
	*/
	public void Mov (IndirectRegister dest, Register source) {
		buffer.WriteByte (0x89);
		dest.EncodeModRm (buffer, source);
	}

	/* mov r/m32, imm32 */ 
	public void Mov (ModRM dest, int imm32) {
		if (dest is Register) {
			buffer.WriteByte ((byte)(0xB8 + ((Register) dest).Index));
		} else {
			buffer.WriteByte (0xC7);
			dest.EncodeModRm (buffer, (byte) 0x0);
		}
		buffer.WriteInt (imm32);
	}
}

}