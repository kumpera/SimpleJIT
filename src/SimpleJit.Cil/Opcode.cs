//
// OpCode.cs
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
using SimpleJit.Extensions;

namespace BasicJit.CIL
{	
	[FlagsAttribute]
	public enum OpcodeFlags {
		Invalid         = 0x1000,

		FlowControlMask = 0x000F,
		Next            = 0x0000,
		Branch          = 0x0001,
		CondBranch      = 0x0002,
		Break           = 0x0003,
		Return          = 0x0004,
		Call            = 0x0005,
		Throw           = 0x0006,

		OpcodeTypeMask  = 0x00F0,
		Macro           = 0x0010,
		ObjectModel     = 0x0020,
		Prefix          = 0x0040,
		Primitive       = 0x0080,

		OperandType     = 0x0F00,
		NoOperand       = 0x0100,
		OperandSize1    = 0x0200,
		OperandSize2    = 0x0300,
		OperandSize4    = 0x0400,
		OperandSize8    = 0x0500,
		OperandSwitch   = 0x0600,
	}

	public struct OpcodeTraits {
		private readonly OpcodeFlags flags;
		private readonly String mnemonic;
		private readonly byte opcode;
		private readonly bool extended;

		public OpcodeTraits (OpcodeFlags flags, String mnemonic, byte opcode, bool extended) {
			this.flags = flags;
			this.mnemonic = mnemonic;
			this.opcode = opcode;
			this.extended = extended;
		}

		public OpcodeFlags Flags { get { return flags; } }
		public String Mnemonic { get { return mnemonic; } }
		public Opcode Opcode { get { return (Opcode)opcode; } }
		public ExtendedOpcode ExtendedOpcode { get { return (ExtendedOpcode)opcode; } }

		public bool IsExtended { get { return extended; } } 
		public bool IsValid { get { return (flags & OpcodeFlags.Invalid) == 0; } }

		internal static void DecodeNext (Stream reader, out OpcodeTraits op) {
			byte cur = (byte)reader.ReadByte ();
			if (cur != (byte)Opcode.ExtendedPrefix)
				TraitsLookup.Decode (cur, out op);
			else
				TraitsLookup.DecodeExtended ((byte)reader.ReadByte (), out op);
		}

		internal int DecodeParamI4 (Stream reader) {
			switch (this.flags & OpcodeFlags.OperandType) {
			case OpcodeFlags.NoOperand:
				throw new Exception ("no param");
			case OpcodeFlags.OperandSize1:
				return reader.ReadByte ();
			case OpcodeFlags.OperandSize2:
				return reader.ReadShort ();
			case OpcodeFlags.OperandSize4:
				return reader.ReadInt ();
			case OpcodeFlags.OperandSize8:
				throw new Exception ("param of size 8");

			case OpcodeFlags.OperandSwitch:
				throw new Exception ("variable length param");
			default:
				throw new Exception ("invalid opcode type " + this.flags);
			}
		}
		
		internal void SkipParams (Stream reader) {
			switch (this.flags & OpcodeFlags.OperandType) {
			case OpcodeFlags.NoOperand:
				break;
			case OpcodeFlags.OperandSize1:
				reader.Skip (1);
				break;
			case OpcodeFlags.OperandSize2:
				reader.Skip (2);
				break;
			case OpcodeFlags.OperandSize4:
				reader.Skip (4);
				break;
			case OpcodeFlags.OperandSize8:
				reader.Skip (8);
				break;
			case OpcodeFlags.OperandSwitch: //FIXME implement me
				throw new Exception ("not supported");
			default:
				throw new Exception ("invalid opcode type " + this.flags);
			}
		}
	}

}
