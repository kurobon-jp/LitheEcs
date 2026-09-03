public static class Check
{
    public static void AreEqual(int expected, int actual)
    {
        if (expected != actual)
            throw new InvalidOperationException($"Expected: {expected}, actual: {actual}.");
    }
}
