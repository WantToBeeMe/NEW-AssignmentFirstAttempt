using System;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
    private const int PORT = 32000; // port of the server
    private const string SERVER_IP = "127.0.0.1";
    private const int BUFFER_SIZE = 1024;
    private const int DATA_CHUNK_SIZE = 100;
    //private const int TIMEOUT_MS = 1000;

    private string applicationDir;
    
    private Socket socket;
    private EndPoint? clientEndPoint;
    private string[]? fileContent; // null means no file requested
    private int slowStartThreshold;
    private int currentWindowSize;
    private int nextDataMessageId; // this-1 is last successfully recieved message. This means that if its missing an ack, the server will start from this message again.
    private bool allDataSent;
    private List<int> acksToReceive = new(); // this is a list of all the acks that we are still waiting for.

    private DateTime windowSentTime; // this is the time when the window was last sent, this is used to check if we need to reset the window size.
    
    public void start()
    {   //DIRK: for some reason this methods name is uncapitalized, just leave it as is
        //DIRK: any comment not written with DIRK or ISSAM is a note that should be deleted later
       
        // DIRK: decide on what to do to get the actual root
        // applicationDir = AppContext.BaseDirectory;
        applicationDir = Path.Combine(AppContext.BaseDirectory, "../../..");
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT));
        
        currentWindowSize = 1;
        nextDataMessageId = 1;
        // ASSIGNMENT: The server will always stay online after terminating the operation waiting for a new Hello.
        while (true) {
            try {
                SingleServerIteration();
            } catch (Exception e) {   
                //DIRK: for now this catches ALL errors, even the ones we didnt make
                HandleException(e);
            }
        }
    }

    private void SingleServerIteration()
    {
        ReceiveHelloMessage();
        
        //TODO: [Send Welcome]
        SendMessageTo(clientEndPoint!, MessageType.Welcome);
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
    private Message ReceiveMessage(MessageType expectedType)
    {
        // This message will handle the generic receive message logic (which is the same for all messages)
        // This method will always return a message that you can asure is of the right type , and has content (if that type should have)
        //      however, checking weather the content is valid is up to the caller. (e.g. checking if the ack content is parsable in to an int)
        // There is 1 special case when the expectedType is MessageType.Hello, in this case we are trying to find a new client, so the the remoteEndPoint is dynamic
        //      for all the other cases we use the clientEndPoint, and check if the received message is from the same client. to make sure its not a new client trying to interfere.
        EndPoint remoteEndPoint = expectedType == MessageType.Hello ? new IPEndPoint(IPAddress.Any, 0) : clientEndPoint!;
        
        byte[] data = new byte[BUFFER_SIZE];
        int bytesReceived = socket.ReceiveFrom(data, ref remoteEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
        );
        
        if (expectedType == MessageType.Hello)
            clientEndPoint = remoteEndPoint;
        else if (clientEndPoint != remoteEndPoint)
        {
            /* ASSIGNMENT: The server will communicate with only one client at a time;
                    it cannot communicate with multiple clients simultaneously; they must interact in sequence.

            This is a special error case since it doesn't really make sence to reset the whole server and stop the connection
                 when some other client tries to connect. As specially when the current connection is still prefectly fine.
                 so instead we just let that new client know that we are buissy and dont act on it further.*/
            SendMessageTo(remoteEndPoint, MessageType.Error,  "Already connected to another client");
            Console.WriteLine("Warning: Other client tried to connect, sent error message and ignored message.");
            return ReceiveMessage(expectedType);
        }
        
        if (message == null)
        {
            /* we set the clientEndPoint here even tho this clause is an exception and it should be reset to null after.
            But we still do since this is the only Error that can happen before the clientEndPoint var is set!!
            and in our case errors are always send to the client, so we need to know where to send this error aswell */
            clientEndPoint = remoteEndPoint;
            ThrowError("Failed to deserialize message", true);
        }
        
        if(message!.Type == MessageType.Error) 
            ThrowError($"Received Error message from client '{message.Content}'", false);
        if (message.Type != expectedType) 
            ThrowError($"Expected {expectedType} message, but received {message.Type}", true);
        if (message.Type != MessageType.End && message.Type != MessageType.Welcome)
        { // END and WELCOME are the only 2 message types that dont have content
            if (string.IsNullOrEmpty(message.Content)) 
                ThrowError("Received empty message, expected there to be content", true);
        }
       
        return message;
    }
    
    //TODO: [Receive Hello]
    private void ReceiveHelloMessage()
    {
        Message message = ReceiveMessage(MessageType.Hello);
        if (!int.TryParse(message.Content, out slowStartThreshold))
            ThrowError("Failed to parse slow start threshold", true);
        
        Console.WriteLine($"Received Hello message ({slowStartThreshold})");
    }
    
    //TODO: [Receive RequestData]
    private void ReceiveRequestDataMessage()
    {
        Message message = ReceiveMessage(MessageType.RequestData);
        string filepath = Path.Combine(applicationDir, message.Content!);
        if (!File.Exists(filepath))
            ThrowError($"File '{message.Content}' not found", true);
        
        fileContent = File.ReadAllText(filepath)     // string
            .Chunk(DATA_CHUNK_SIZE)                  // char[][]
            .Select(chunk => new string(chunk)) // string[]
            .ToArray();                              // string[]
        
        Console.WriteLine($"Received RequestData message from client. '{message.Content}'");
    }
 
    //TODO: [Receive Ack]
    private void ReceiveAckMessage()
    {
        Message message = ReceiveMessage(MessageType.Ack);
        if (!int.TryParse(message.Content, out int ackId))
            ThrowError("Failed to parse ack id", true);
        
        // The assignment only states that missing acks are not errors. But nothing is said about acks that we are not supose to get
        // But if it turns out we are not supose to throw an error here, we can simply replace this ThrowError() with a return statement.
        if (ackId < nextDataMessageId || ackId > nextDataMessageId + currentWindowSize)
            ThrowError("Received ack was not send in the current window", true);
        
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
        Console.WriteLine($"SENDING WINDOW ({currentWindowSize}): {nextDataMessageId} - {(nextDataMessageId + currentWindowSize)}");
        acksToReceive.Clear();
        windowSentTime = DateTime.Now; // this is the time when the window was last sent, this is used to check if we need to reset the window size.
        for (int i = 0; i < currentWindowSize; i++)
            allDataSent = !SendSingleDataMessage(nextDataMessageId+i);
        
        while (acksToReceive.Count != 0)
            {
                ReceiveAckMessage();

                // Check the timeout 
                if ((DateTime.Now - windowSentTime).TotalMilliseconds > 1000) // 1 sec timeout
                {
                    // Reset the window 
                    currentWindowSize = 1;

                    // Set nextDataMessageId to the lowest missing ACK
                    nextDataMessageId = acksToReceive.Min();

                    // Set allDataSent to false to continue sending data :)
                    allDataSent = false;

                    break;
                }
            }
        
        nextDataMessageId += currentWindowSize;
        currentWindowSize = Math.Min(currentWindowSize * 2, slowStartThreshold);
        
        //DIRK: Implement the timout here. If the server did not receive all the acks in time:
        //      - reset the window size to 1
        //      - set the nextDataMessageId to the lowest number in the acksToReceive list (which is the first ack that was not received)
        //      - set allDataSent to false
        // that's all you have to do (literally (re)set 3 variables), since if you reset it correctly, the next loop will follow since allDataSent is false, and thus it will follow where we left off.
        
    }
    
    //TODO: [Send End]
    private void SendEndMessage()
    {
        SendMessageTo(clientEndPoint!, MessageType.End);
        Console.WriteLine("Sent End message to client.");
    }
    
    //TODO: [Handle Errors]
    /* ASSIGNMENT: The Error message is there to communicate to either of the other party that there was an error,
           please be specific about what is the error in the content of the message. Upon reception of an
           error, the server will reset the communication (and be ready again). The client will terminate
           printing the error */
    private void ThrowError(string description,  bool notifyClient)
    {
       if (notifyClient) 
           SendMessageTo(clientEndPoint!, MessageType.Error, description);
       
       // We throw an error, this then gets caught in the HandleException method
       // The reason for this is to ensure that all code coming after the error is not executed,
       //   and this without having to put the return statement in every if clause that checks for an error.
       // The exception is then caught in the HandleException method, which will reset the server without actually throwing it further,
       //   which will make the server run normally again in the next iteration of the while loop.
       throw new Exception(description);
    }
    
    private void HandleException(Exception e)
    {
        Console.WriteLine($"Error: {e.Message}");
        Console.WriteLine("Resetting server...");
             
        currentWindowSize = 1;
        nextDataMessageId = 1;
        slowStartThreshold = 0;
        fileContent = null;  
        clientEndPoint = null;
        acksToReceive.Clear();
    }
}
