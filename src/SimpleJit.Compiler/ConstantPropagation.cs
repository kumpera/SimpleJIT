using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleJit.Compiler {

public static class CollectionExtensions {
	public static V TryGet<K,V> (this IDictionary<K,V> dict, K k)
	{
	    V v;
	    return dict.TryGetValue(k, out v) ? v : default (V);
	}
}

public class CpropPass {
	Compiler cc;


	public CpropPass (Compiler cc) {
		this.cc = cc;
	}

	void UpdateCI (CallInfo ci, Dictionary<int, Ins> consts) {
		ci.CpropValues = new List<Ins> ();
		Console.WriteLine ($"Updating {ci}");
		for (int i = 0; i < ci.Args.Count; ++i) {
			Console.WriteLine ($"\tCI[{i}] = {ci.Args [i]} -> {consts.TryGet (ci.Args [i])}");
			ci.CpropValues.Add (consts.TryGet (ci.Args [i]));
		}
	}

	Ins Prop (Dictionary<int, Ins> consts, BasicBlock bb, Ins ins) {
		switch (ins.Op) {
		case Ops.IConst: {
			consts [ins.Dest] = ins;
			Console.WriteLine ($"\tR{ins.Dest} [const]");
			break;
		}
		case Ops.Add: {
			var v0 = consts.TryGet (ins.R0);
			var v1 = consts.TryGet (ins.R1);
			if (v0 != null && v1 != null) {
				return new Ins (Ops.IConst) {
					Dest = ins.Dest,
					Const0 = v0.Const0 + v1.Const0
				};
			}
			if (v0 != null || v1 != null) {
				int c = 0;
				int r = 0;
				if (v0 != null) {
					c = v0.Const0;
					r = ins.R1;
				} else {
					c = v1.Const0;
					r = ins.R0;
				}
				return new Ins (Ops.AddI) {
					Dest = ins.Dest,
					R0 = r,
					Const0 = c
				};
			}
			break;
		}
		case Ops.AddI: {
			var v0 = consts.TryGet (ins.R0);
			if (v0 != null) {
				return new Ins (Ops.IConst) {
					Dest = ins.Dest,
					Const0 = ins.Const0 + v0.Const0
				};
			}
			break;
		}
		case Ops.Ble:
		case Ops.Blt:
		case Ops.Bg:
		case Ops.Bge:
		case Ops.Br: {
			foreach (var ci in ins.CallInfos)
				UpdateCI (ci, consts);
			break;
		}
		}
		return null;
	}

	void PropagateValues (BasicBlock bb) {
		Console.WriteLine ($"CPROP BB{bb.Number}");

		var consts = new Dictionary<int, Ins> ();
		//FIXME do something fancy when there are more than one previous block
		if (bb.From.Count == 1) {
			CallInfo ci = bb.From[0].InfoFor (bb);
			for (int i = 0; i < ci.Args.Count; ++i) {
				if (ci.CpropValues [i] != null) {
					Console.WriteLine ($"\tiniting [{i}] with ({ci.CpropValues [i]})");
					consts [i] = ci.CpropValues [i];
				}
			}
		}


		for (Ins ins = bb.FirstIns; ins != null; ins = ins.Next) {
			while (true) {
				Ins r = Prop (consts, bb, ins);
				if (r == null)
					break;
				Console.WriteLine ($"replacing ({ins}) with ({r})");
				bb.ReplaceWith (ins, r);
				ins = r;
			}
		}
		
	}

	public void Run () {
		Console.WriteLine ("CpropPass");
		cc.ForwardPropPass (PropagateValues);
		cc.Dump ("AFTER CPROP");
	}
}

}
