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
    private const int PORT = 32000;
    private const string SERVER_IP = "127.0.0.1";
    private const int BUFFER_SIZE = 1024;
    private const int DATA_CHUNK_SIZE = 100;
    private const int ACK_TIMEOUT_MS = 3000;

    private string applicationDir;
    
    private Socket socket;
    private EndPoint? clientEndPoint;
    private string[]? fileContent; // null means no file currently being requested
    private int slowStartThreshold;
    private int currentWindowSize;
    private int nextDataMessageId; // this-1 is last successfully recieved message. This means that if its missing an ack, the server will start from this message again.
    private bool allDataSent;
    private List<int> acksToReceive = new(); // this is a list of all the acks that we are still waiting for.
    
    public void start()
    {   
        // DIRK: decide on what to do to get the actual root
        // applicationDir = AppContext.BaseDirectory;
        applicationDir = Path.Combine(AppContext.BaseDirectory, "../../..");
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT));

        ResetServer(true);
        
        // Assignment: "The server will always stay online after terminating the operation waiting for a new Hello."
        //    so that's why the infinite loop 
        while (true) {
            try {
                SingleServerIteration();
            } catch (Exception e) {   
                // if there is any error (lets say Received Request before Hello), then we throw an error, and catch it here.
                //  currently we catch ALL errors (even once we didnt make), and reset the server,
                //  this is because the assignment states that the server should always stay online.
                //  but a future dev could easily create its own exceptions class and only catch the ones that are thrown by the server.
                Console.WriteLine($"Error: {e.Message}");
            }
            ResetServer();
        }
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
        // This message will handle the generic receive message logic (which is the same for all messages)
        // This method will always return a message that you can asure is of the right type , and has content (if that type should have)
        //      however, checking weather the content is valid is up to the caller. (e.g. checking if the ack content is parsable in to an int)
        // There is 1 special case when the expectedType is MessageType.Hello, in this case we are trying to find a new client, so the the remoteEndPoint is dynamic
        //      for all the other cases we use the clientEndPoint, and check if the received message is from the same client. to make sure its not a new client trying to interfere.
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
        string filepath = Path.Combine(applicationDir, message.Content!);
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
        
        // The assignment only states that missing acks are not errors. But nothing is said about acks that we are not supose to get
        // But if it turns out we are not supose to throw an error here, we can simply do 1 of 2 things:
        // 1. replace this ThrowError() with a return statement.
        // 2. completely remove this clause
        // it is implemented in a way that it does not really matter which of the 3 (thisone included) options you choose.
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
       
       // We throw an error, this then gets caught and will reset the server.
       //   When there is an error, you dont want all the code after it to keep running,
       //   Throwing and catching errors is easier then using return statements in every if clause that checks for an error.
       //   and is less prone to bugs since you dont have to remember to add a return statement in every if clause.
       throw new Exception(description);
    }
    
    private void ResetServer(bool firstTimeStarting = false)
    {
        if(!firstTimeStarting)
            Console.WriteLine("Resetting server...");
        currentWindowSize = 1;
        nextDataMessageId = 1;
        slowStartThreshold = 0;
        fileContent = null;  
        clientEndPoint = null;
        acksToReceive.Clear();
    }
}
