using System.Collections.Generic;

public class InputBuffer
{
    private Queue<ClientInput> inputQueue = new Queue<ClientInput>();

    public void AddInput(ClientInput input)
    {
        inputQueue.Enqueue(input);
    }

    public ClientInput GetNextInput()
    {
        if (inputQueue.Count > 1)
        {
            return inputQueue.Dequeue();
        }
        return default(ClientInput); // Default value if no inputs are available
    }

    public bool HasInput()
    {
        return inputQueue.Count > 0;
    }
}
