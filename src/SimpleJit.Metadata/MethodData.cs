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

public class MethodData {
	Image image;
	int rva;
	int implFlags, flags;
	string name;
	Signature signature;

	public MethodData (Image image, int index) {
		var row = new MethodDefRow ();
		row.Read (image, index);
		this.image = image;
		this.rva = (int)row.rva;
		this.implFlags = row.implFlags;
		this.flags = row.flags;
		this.name = image.DecodeString (row.name);
		this.signature = image.LoadSignature ((int)row.signature);
		// this.paramList = image.ReadParamList (row.ParamList);
	}

	public string Name {
		get { return name; }
	}

	public override string ToString () {
		return $"method-def {this.name} implFlags: {this.implFlags:X} flags: {this.flags:X} sig: {this.signature}";
	}

	public MethodBody GetBody () {
		return image.LoadMethodBody (rva);
	}

	public Image Image {
		get { return image; }
	}

	public Signature Signature {
		get { return signature; }
	}

}

}