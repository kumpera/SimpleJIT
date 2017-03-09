using SimpleJit.Testing;

public static class Foo {
	public static int test_0_return_const ()
	{
		return 0;
	}

	public static int test_7_add_vals ()
	{
		int a = 4;
		int b = 3;
		int c = a + b;
		return c;
	}

	public static int test_9_add_const ()
	{
		int a = 4;
		int c = a + 5;
		return c;
	}

	public static int test_4_1_3_add_arguments (int a, int b)
	{
		return a + b;
	}

	public static int test_2_cond ()
	{
		int a = 10;
		int b = 20;
		if (a > b)
			return 1;
		return 2;
	}

	public static int test_1_cond2 ()
	{
		int a = 11;
		int b = 10;
		if (a > b)
			return 1;
		return 2;
	}

	public static int test_2_comp_with_const ()
	{
		int a = 10;
		if (a > 20)
			return 1;
		return 2;
	}

	public static int test_1_comp_with_const2 ()
	{
		int a = 10;
		if (20 > a)
			return 1;
		return 2;
	}

	public static int test_1_10_20_comp_with_const2 (int a, int b)
	{
		int c = a;
		if (b > c)
			return 1;
		return 2;
	}

	public static int test_3_1_44_cond_with_add (int a, int b)
	{
		int c = 0;
		if (a > 0)
			c = a;
		else
			c = b;
		return c + 2;
	}

	public static int test_4_0_2_cond_with_add (int a, int b)
	{
		int c = 0;
		if (a > 0)
			c = a;
		else
			c = b;
		return c + 2;
	}

	public static int test_45_simple_counting_loop () {
		int res = 0;
		for (int i = 0; i < 10; ++i) {
			res += i;
		}
		return res;
	}

	public static int test_35_12_loop_with_limit (int limit) {
		int acc = 0;
		for (int i = 0; i < limit; ++i) {
			if (i > 10)
				acc += 2;
			else
				acc += 3;
		}
		return acc;
	}

	public static int test_30_call_other () {
		return EarlyReturn (5);
	}

	public static int EarlyReturn (int v) {
		if (v > 10)
			return 20;
		return 30;
	}

	public static int test_23_1_2_cprop_dce (int a, int b) {
		int d = 10;
		int e = 20;
		int c = 0;
		d = d + 1;
		e = e + 1;
		if (a > b)
			c = a + d;
		else
			a = b + e;
		return c + a;
	}
}
