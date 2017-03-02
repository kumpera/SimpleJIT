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

}
