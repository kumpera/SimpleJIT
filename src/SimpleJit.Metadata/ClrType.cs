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

public enum ElementType {
	Void = 0x01,
	Int32 = 0x08,
	String = 0x0e,
	SzArray = 0x1d,
	CMOD_REQD = 0x1f,
	CMOD_OPT = 0x20,

}

public abstract class ClrType {
	public virtual ElementType ElementType { get; }

	public static readonly ClrType Void = new PrimitiveType (ElementType.Void);
	public static readonly ClrType Int32 = new PrimitiveType (ElementType.Int32);
	public static readonly ClrType String = new TypeDefinition ("System", "String");


	public static ClrType NewSzArray (ClrType[] cmod, ClrType elem) {
		if (cmod != null)
			throw new Exception ("cmod not supported!");
		return new ArrayType (elem);
	}
}

public class ArrayType : ClrType {
	ClrType elementType;

	public ArrayType (ClrType elementType) {
		this.elementType = elementType;
	}
}

public class TypeDefinition : ClrType {
	string ns, name;

	public TypeDefinition (string ns, string name) {
		this.ns = ns;
		this.name = name;
	}
}
public class PrimitiveType : ClrType {
	ElementType type;
	internal PrimitiveType (ElementType type) {
		this.type = type;
	}

	public override ElementType ElementType { get { return type; } }

	public override string ToString () {
		return type.ToString ();
	}
}
}
