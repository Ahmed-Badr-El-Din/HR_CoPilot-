using System;

class Program
{
    static void Main()
    {
        string[] candidates = { "candidateA", "candidateB", "candidateC" };
        foreach (var c in candidates)
        {
            var seed = c.GetHashCode();
            Console.WriteLine($"{c} - {seed}");
            
            for(int i=0; i<2; i++) {
                var dimId = "dim-" + i;
                var combined = Math.Abs(seed ^ (dimId.GetHashCode() * 31) ^ (i * 7919));
                var score = 1 + (combined % 2);
                Console.WriteLine($"  {dimId}: score={score}");
            }
        }
    }
}
