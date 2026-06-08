namespace PeerLend.Workers;

public class Program
{
    // The explicit static Main method serves as the entry point for the background 
    // worker process. This host will run our Hangfire distributed queues.
    public static void Main(string[] args)
    {
        // This log confirms the console application has booted into memory successfully
        Console.WriteLine("Initializing PeerLend Background Workers Process Engine...");

        // Phase 1 implementation requires this worker thread to stay alive in perpetuity.
        // We will construct our dependency injection containers and Hangfire server options here.
        Console.WriteLine("Workers engine running. Press Ctrl+C to safely terminate the process loop.");

        // This line halts execution so the console window does not immediately pop open and close
        while (true)
        {
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
