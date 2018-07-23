# Intro

This is a prototype compiler to validate if EBBs with arguments (an extreme variant of SSI) is usefull.


# DONE
	dump spill slots on RA state to ensure we're not ignoring them when handling CallInfos
	n-var repairing - done for trivial repair.
	spilling //basic done, lots of corner cases left TBH
	implement cprop, dce and isel as part of the front-end -- done, ishy, cprop and isel, no DCE
	calls
	2 pass alloc (forward pass for prefs, backward pass for alloc)
	Generate full assembly that can then be used to test the results.

# GOALS
	Show some aggressive high level opts.
	Produce AOT images compat with mono as a final POC for this.

# Actionable TODO (actual tasks I can do in 1-2 hours without having to onboard back too much)
	Figure out the state of IRGen and next steps.


# TODO
	Fix SpillConst so Const0 is always the address (not the case for other ops - ?maybe go full typed args?)
	Keep poking at the RA table design
	Merge the 2 ra branch funcs (DirectBranch and CondBranch)
	Add clob support (support SetRet and Mul/Div)
		Actually, this already happens with DA1 x DA1Clob (mov x addi)
	Kill LoadArg and SetRet and use in/out BB args instead?
	The whole regalloc probably needs to be rewritten as it mixes spilling, reg alloc and insel all together.

	** The goal is to get enough in place that adding mul and div happens only though ir-emit
		it's all messed up <o>

 	LVN in the front-end
	DCE and x-block const prop

	3 pass alloc that does backwards for liveness, then forward for prefs, then backwards again for alloc.
		The first backwards pass would allow us to give callee-saved prefs and some spill heuristics.
	actual DCE, regalloc gets pissed off with dead dregs
	critical edges
	valuetypes
	byref
	floating point
	more ops
	let the regalloc change encoding (add reg, reg -> add reg, [spill slot])
	iterated alloc in case of bad decision (too much repair?)
	do some of the spilling during the prefs pass.

# Current design flaws

The use of LoadArg sucks as it is the same reg shuffling problem of repairing and it doesn't allow for a global decision to be made.
	-If we support external allocation of BB::InVars (as a way to handle LCOV), this becomes a common case
