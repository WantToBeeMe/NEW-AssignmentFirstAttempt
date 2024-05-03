using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MessageNS;

// Students:
// student 1: Dirk Roosendaal - 1031349
// student 2: Issam Ben Massoud - 1055156

// do not modify this class
class Program
{
    static void Main(string[] args)
    {
        ServerUDP sUDP = new ServerUDP();
        sUDP.start();
    }
}

class ServerUDP
{
    //TODO: implement all necessary logic to create sockets and handle incoming messages
    //TODO: create all needed objects for your sockets 
    private const int PORT = 32000;
    private const string SERVER_IP = "127.0.0.1";
    private const int BUFFER_SIZE = 1024;
    private const int DATA_CHUNK_SIZE = 100;
    private const int ACK_TIMEOUT_MS = 3000;

    private string applicationDirectory;
    private Socket socket;
    private EndPoint? clientEndPoint;
    private string[]? fileContent; // null means no file currently being requested
    private int slowStartThreshold;
    private int currentWindowSize;
    private int nextDataMessageId; // this-1 is last successfully received message. This means that if its missing an ack, the server will start from this message again.
    private bool allDataSent;
    private List<int> acksToReceive; // this is a list of all the ack messages that we are still waiting for.
    
    public void start()
    {   
        // the application "root directory"  we assume is the directory where also all the code it stored ( Assignment/server/* )
        // not where the code is actually run ( Assignment/server/bin/Debug/net6.0/* )
        // This means we have to go back 3 folders to get to the root directory, there is also an other way of doing this.
        // which is modifying the server.csproj to copy the file to the output direction. but this seems to not fit the assignment since we are only allowed to modify this code. 
        applicationDirectory = Path.Combine(AppContext.BaseDirectory, "../../..");
        acksToReceive = new List<int>();
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT));
        ResetServer();
        
        // Assignment: "The server will always stay online after terminating the operation waiting for a new Hello."
        //    so that's why the infinite loop 
        while (true) {
            try {
                SingleServerIteration();
            } catch (Exception e) {   
                // Explanation about decisions we made for errors, is in the HandleError method at the bottom of the code.
                //  things like: why we throw and catch errors instead of handling them in-place,
                //  or: why do we catch ALL errors, and not just errors we made ourselves.
                Console.WriteLine($"Error: {e.Message}");
            }
            // errors will just stop the current server iteration. without running the code that was still yet to run in that iteration.
           
            // the server will reset after every iteration, regardless if there was an error or not.
            Console.WriteLine("Resetting server...");
            ResetServer();
        }
    }
    
    private void ResetServer()
    {
        currentWindowSize = 1;
        nextDataMessageId = 1;
        slowStartThreshold = 0;
        fileContent = null;  
        clientEndPoint = null;
        acksToReceive.Clear();
    }
    
    private void SingleServerIteration()
    {
        ReceiveHelloMessage();
        SendHelloWelcome();
        ReceiveRequestDataMessage();
        
        allDataSent = false;
        while (!allDataSent)
            SingleAlgorithmStep();
        
        SendEndMessage();
    }
    
    private void SendMessageTo(EndPoint endPoint, MessageType type, string? content = null)
    {
        Message message = new Message { Type = type, Content = content };
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        socket.SendTo(data, endPoint);
    }

    //TODO: keep receiving messages from clients
    private Message ReceiveMessage(MessageType expectedType, int timeout = 0)
    {
        // This method handles all the generic receive message logic. when you run this you can assume the following.
        //  1. The message is of the expected type
        //  2. The message has content (if that type should have)
        //  3. The message is from the right client (if the expectedType is not MessageType.Hello)
        //  4. The message is not an error message
        // If any of these assumptions are not true, an error will be thrown and the server will reset itself.
        EndPoint remoteEndPoint = expectedType == MessageType.Hello ? new IPEndPoint(IPAddress.Any, 0) : clientEndPoint!;
        
        byte[] data = new byte[BUFFER_SIZE];
        socket.ReceiveTimeout = timeout;
        int bytesReceived = socket.ReceiveFrom(data, ref remoteEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
        );
        
        if (expectedType == MessageType.Hello)
            clientEndPoint = remoteEndPoint;
        else if (clientEndPoint != remoteEndPoint)
        {
            /* Assignment: The server will communicate with only one client at a time;
                it cannot communicate with multiple clients simultaneously; they must interact in sequence.

            This is a special error case since it doesn't really make sense to reset the whole server and stop the connection
                 when some other client tries to connect. As specially when the current connection is still perfectly fine.
                 so instead we just let that new client know that we are busy and dont act on it further.*/
            SendMessageTo(remoteEndPoint, MessageType.Error,  "Already connected to another client");
            Console.WriteLine("Warning: Other client tried to connect, sent error message and ignored message. Server will continue as normal.");
            return ReceiveMessage(expectedType);
        }
        
        if (message == null)
        {
            // We set the clientEndPoint here even tho this clause is an exception and it should be reset to null after.
            // We still do it since the HandleError method needs to know where to send the error to.
            clientEndPoint = remoteEndPoint;
            HandleError("Failed to deserialize message", true);
        }
        
        if(message!.Type == MessageType.Error) 
            HandleError($"Received Error message from client '{message.Content}'", false);
        if (message.Type != expectedType) 
            HandleError($"Expected {expectedType} message, but received {message.Type}", true);
        if (message.Type != MessageType.End && message.Type != MessageType.Welcome)
        { // END and WELCOME are the only 2 message types that dont have content
            if (string.IsNullOrEmpty(message.Content)) 
                HandleError("Received empty message, expected there to be content", true);
        }
       
        return message;
    }

    //TODO: [Send Welcome]
    private void SendHelloWelcome()
    {
        SendMessageTo(clientEndPoint!, MessageType.Welcome);
        Console.WriteLine("Sent Welcome message to client.");
    }
    
    //TODO: [Receive Hello]
    private void ReceiveHelloMessage()
    {
        Message message = ReceiveMessage(MessageType.Hello);
        if (!int.TryParse(message.Content, out slowStartThreshold))
            HandleError("Failed to parse slow start threshold", true);
        
        Console.WriteLine($"Received Hello message ({slowStartThreshold})");
    }
    
    //TODO: [Receive RequestData]
    private void ReceiveRequestDataMessage()
    {
        Message message = ReceiveMessage(MessageType.RequestData);
        string filepath = Path.Combine(applicationDirectory, message.Content!);
        if (!File.Exists(filepath))
            HandleError($"File '{message.Content}' not found", true);
        
        fileContent = File.ReadAllText(filepath)     // string
            .Chunk(DATA_CHUNK_SIZE)                  // char[][]
            .Select(chunk => new string(chunk)) // string[]
            .ToArray();                              // string[]
        Console.WriteLine($"Received RequestData message from client. '{message.Content}'");
    }
 
    //TODO: [Receive Ack]
    private void ReceiveAckMessage(int timeout)
    {
        Message message = ReceiveMessage(MessageType.Ack, timeout);
        if (!int.TryParse(message.Content, out int ackId))
            HandleError("Failed to parse ack id", true);
        
        // The assignment only states that missing ack messages are not errors. But nothing is said about an ack that we are not suppose to get
        // But if it turns out we are not suppose to throw an error here, we can simply do 1 of 2 things:
        // 1. replace this ThrowError() with a return statement.
        // 2. completely remove this clause
        // it is implemented in a way that it does not really matter which of the 3 (this one included) options you choose.
        if (ackId < nextDataMessageId || ackId > nextDataMessageId + currentWindowSize)
            HandleError("Received ack was not send in the current window", true);
        
        Console.WriteLine($"Received Ack for message {ackId}");
        acksToReceive.Remove(ackId);
    }
    
    //TODO: [Send Data]
    private bool SendSingleDataMessage(int dataMessageId)
    {
        if (dataMessageId > fileContent?.Length) return false;
        string stringId = dataMessageId.ToString("D4");
        string content = fileContent![dataMessageId - 1];
        SendMessageTo(clientEndPoint!, MessageType.Data, $"{stringId}{content}");
        Console.WriteLine($"SEND - {stringId}: {content.Replace("\n", "\\n")}");
        acksToReceive.Add(dataMessageId);
        return true;
    }
    
    //TODO: [Implement your slow-start algorithm considering the threshold]
    private void SingleAlgorithmStep()
    {
        Console.WriteLine($"SENDING WINDOW ({currentWindowSize}): {nextDataMessageId} - {(nextDataMessageId + currentWindowSize - 1)}");
        acksToReceive.Clear();
        for (int i = 0; i < currentWindowSize; i++)
            allDataSent = !SendSingleDataMessage(nextDataMessageId+i);
        
        while (acksToReceive.Count != 0) 
        {
            try {
                ReceiveAckMessage(ACK_TIMEOUT_MS);
            } catch (SocketException ex) 
            {
                // If the error was not a timeout, we still have to throw it and reset the server.
                if (ex.SocketErrorCode != SocketError.TimedOut)
                    HandleError(ex.Message, true);
                
                currentWindowSize = 1;
                nextDataMessageId = acksToReceive.Min(); // our next window should start at the first missing ack
                allDataSent = false;
                
                Console.WriteLine($"Timeout, resetting window and start sending from ack: {nextDataMessageId}");
                return;
            }
        }
        
        nextDataMessageId += currentWindowSize;
        currentWindowSize = Math.Min(currentWindowSize * 2, slowStartThreshold);
    }
    
    //TODO: [Send End]
    private void SendEndMessage()
    {
        SendMessageTo(clientEndPoint!, MessageType.End);
        Console.WriteLine("Sent End message to client.");
    }
    
    //TODO: [Handle Errors]
    private void HandleError(string description,  bool notifyClient)
    {
       if (notifyClient) 
           SendMessageTo(clientEndPoint!, MessageType.Error, description);
       
       // The error gets printed to the console in catch clause
       //  (to make sure that other errors not from here also get printed to the console)
       //  this will then stop the current server iteration.
       throw new Exception(description);
       
       // WHY DO WE THROW ERRORS AND NOT JUST HANDLE THEM IN-PLACE?
       //   We throw an error, this then gets caught at toplevel of the while-loop, skipping all the code was yet to run.
       //   When there is an error, you dont want all the code after it to keep running,
       //   Throwing and catching errors is easier then using return statements in every if clause that checks for an error.
       //   and is less prone to bugs since you dont have to remember to add a return statement in every if clause.
       
       // WHY DO WE CATCH ALL ERRORS AND NOT JUST THE ONES WE MADE OURSELVES?
       //   You normally dont `catch(Exception e)` since this will catch all errors, not differentiating between the ones.
       //   However, we decided to catch all errors anyway, this is because the assignment states that the server should ALWAYS stay online.
       //   But if we would only want to catch errors made by us and not all of them, then we would create our own exceptions like so:
       //       class ServerException : Exception { }
       //   and throw/catch them instead.
    }
}
