//
// MethodData.cs
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
using SimpleJit.CIL;

namespace SimpleJit.Metadata {

public class Signature {
	///XXX promote this out next time we decode blob stuff
	struct BlobDecoder {
		byte[] data;
		int idx;
		int end;

		internal BlobDecoder (Image image, int index) {

			data = image.data;
			this.idx = (int)image.GetBlobIndex (index);
			this.end = 0;

			int len = ReadInt ();
			end = idx + len;
		}

		internal int ReadInt () {
			int b = data [idx];
			if ((b & 0x80) == 0) {
				idx += 1;
				return b;
			} else if ((b & 0x40) == 0) {
				int res = (b & 0x3f) << 8 | (data[idx + 1]);

				idx += 2;
				return res;
			} else {
				int res = ((b & 0x1f) << 24) |
					(data [idx + 1] << 16) |
					(data [idx + 2] << 8) |
					 data [idx + 3];

				idx += 4;
				return res;
			}
		}

		internal int ReadByte () {
			return data[idx++];
		}

		internal ClrType[] ReadCustomMod () {
			return null; //FIXME
		}

		internal ClrType ReadType () {
			ElementType t;
			switch (t = (ElementType)ReadByte ()) {
			case ElementType.CMOD_REQD:
			case ElementType.CMOD_OPT:
				throw new Exception ("Can't handle custom modifiers");
			case ElementType.Void:
				return ClrType.Void;
			case ElementType.Int32:
				return ClrType.Int32;
			case ElementType.String:
				return ClrType.String;
			case ElementType.SzArray: {
				ClrType[] cmod = ReadCustomMod ();
				ClrType etype = ReadType ();
				return ClrType.NewSzArray (cmod, etype);
			}

			default:
				throw new Exception ($"Can't decode type {t:X}"); 
			}
		}

	}

	byte flags;
	ClrType ret;
	ClrType[] parameters;

	public Signature (Image image, int index) {
		var bd = new BlobDecoder (image, index);

		flags = (byte)bd.ReadByte ();

		if (HasGenericParams)
			throw new Exception ("Not decoding gparams");

		int paramCount = bd.ReadInt ();
		this.ret = bd.ReadType ();
		this.parameters = new ClrType [paramCount];
		for (int i = 0; i < paramCount; ++i)
			this.parameters [i] = bd.ReadType ();
	}

	public bool HasGenericParams { 
		get { return (flags & 0x10) != 0; }
	}

	public bool HasThis { 
		get { return (flags & 0x20) != 0; }
	}

	public bool HasExplicitThis { 
		get { return (flags & 0x40) != 0; }
	}

	public CallingConvention CallingConvention {
		get { return (CallingConvention)(flags & 0xF); }
	}

	public int ParamCount {
		get { return this.parameters.Length; }
	}

	public ClrType ReturnType {
		get { return ret; }
	}

	public override string ToString () {
		string res = "";

		if (HasGenericParams)
			res += "generic ";
		if (HasThis)
			res += "this ";
		if (HasExplicitThis)
			res += "explicit ";

		res += CallingConvention + " ";
		res += ret.ToString ();
		res += " (";
		for (int i = 0; i < parameters.Length; ++i) {
			if (i > 0)
				res += ", ";
			res += parameters [i].ToString ();
		}
		res += ")";
		return res;
	}
}

public enum CallingConvention {
	Default = 0,
	Vararg = 5,
}
}