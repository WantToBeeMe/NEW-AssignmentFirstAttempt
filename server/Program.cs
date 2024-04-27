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
// student 2:

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
    private const int BUFFER_SIZE = 1024;
    //private const int DATA_CHUNK_SIZE = 100;
    //private const int TIMEOUT_MS = 1000;

    private string applicationDir;
    
    private Socket socket;
    private EndPoint? clientEndPoint;
    private string fileContent;
    private int slowStartThreshold;
    private int currentWindowSize;
    private int nextDataMessageId;

    public void start()
    {   //DIRK: for some reason this methods name is uncapitalized, just leave it as is
        //DIRK: any comment not written with DIRK or ISSAM is a note that should be deleted later
       
        // DIRK: decide on what to do to get the actual root
        // applicationDir = AppContext.BaseDirectory;
        applicationDir = Path.Combine(AppContext.BaseDirectory, "../../..");
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), PORT));
        
        // ASSIGNMENT: The server will always stay online after terminating the operation waiting for a new Hello.
        while (true) {
            try {
                RecieveMessage();
            } catch (Exception e) {   
                //DIRK: for now this catches ALL errors, even the ones we didnt make
                HandleException(e);
            }
        }
        
    }
    
    //TODO: keep receiving messages from clients
    // you can call a dedicated method to handle each received type of messages
    private void RecieveMessage()
    {
        EndPoint remoteEndPoint = clientEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
        byte[] data = new byte[BUFFER_SIZE];
        int bytesReceived = socket.ReceiveFrom(data, ref remoteEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
            );
 
        if(clientEndPoint != null && clientEndPoint != remoteEndPoint)
        {
            /* ASSIGNMENT: The server will communicate with only one client at a time;
                    it cannot communicate with multiple clients simultaneously; they must interact in sequence. */
            
            // This is a special error case since it doesn't really make sence to reset the whole server
            //      when some other client tries to connect, even tho the current connection is prefectly fine.
            // so instead we just let that client know that we are buissy and dont act on it further.
            string description = "Already connected to another client";
            Message errorMessage = new Message { Type = MessageType.Error, Content = description };
            byte[] sendData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(errorMessage));
            socket.SendTo(sendData, remoteEndPoint);
            Console.WriteLine("Warning: Other client tried to connect, sent error message and ignored message.");
            return;
        }
        
        if (message == null)
        {
            // we set the clientEndPoint here even tho this clause is an exception and it should be reset to null after.
            // But we still do since this is the only Error that can happen before the clientEndPoint var is set!!
            // and in our case errors are always send to the client, so we need to know where to send this error aswell
            clientEndPoint = remoteEndPoint;
            ThrowError("Failed to deserialize message", true);
        }
        switch (message.Type)
        {
            case MessageType.Hello:
                RecieveHelloMessage(message, remoteEndPoint);
                break;
            case MessageType.RequestData:
                ReceiveRequestDataMessage(message);
                break;
            case MessageType.Ack:
                Console.WriteLine("Received Ack");
                break;
            case MessageType.Error:
                ThrowError($"Received Error message from client '{message.Content}'", false);
                break;
            default:
                ThrowError($"Received unexpected message type '{message.Type}' with content '{message.Content}'", true);
                break;
        }
    }

    //TODO: [Receive Hello]
    private void RecieveHelloMessage(Message message, EndPoint remoteEndPoint)
    {
        if(clientEndPoint != null)
        {
            ThrowError("Client already connected, did not expect a second hello message", true);
        }
        clientEndPoint = remoteEndPoint;
        Console.WriteLine("Received Hello message");
        if (!int.TryParse(message.Content, out slowStartThreshold))
        {
            ThrowError("Failed to parse slow start threshold", true);
        }
        currentWindowSize = 1;
        nextDataMessageId = 1;
   
        SendWelcomeMessage();
    }
    //TODO: [Send Welcome]
    private void SendWelcomeMessage()
    {
        // ASSIGNMENT-PROTOCOL 2. The server replies with a Welcome-message 
        Message welcomeMessage = new Message { Type = MessageType.Welcome };
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(welcomeMessage));
        socket.SendTo(data, clientEndPoint);
    }
    
    //TODO: [Receive RequestData]
    private void ReceiveRequestDataMessage(Message message)
    {
        if (string.IsNullOrEmpty(message.Content))
        {
            ThrowError("No file specified", true);
        }
        string filepath = Path.Combine(applicationDir, message.Content);
        if (!File.Exists(filepath))
        {
            ThrowError($"File '{message.Content}' not found", true);
        }
        Console.WriteLine($"Received RequestData message from client. '{message.Content}'");
        fileContent = File.ReadAllText(filepath);
        
        ThrowError("er is geen error hier, ik heb alleen een manier nodig om de server te resetten, groetjes dirk, line 162", false);
    }
    //TODO: [Send Data]

    //TODO: [Implement your slow-start algorithm considering the threshold] 

    //TODO: [End sending data to client]

    //TODO: [Handle Errors]
    /* ASSIGNMENT: The Error message is there to communicate to either of the other party that there was an error,
           please be specific about what is the error in the content of the message. Upon reception of an
           error, the server will reset the communication (and be ready again). The client will terminate
           printing the error */
    private void ThrowError(string description,  bool notifyClient)
    {
       if (notifyClient)
       {
           Message errorMessage = new Message { Type = MessageType.Error, Content = description };
           byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(errorMessage));
           socket.SendTo(data, clientEndPoint);
       }
        
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
             
        currentWindowSize = 1;
        nextDataMessageId = 1;
        slowStartThreshold = 0;
        fileContent = null;  
        clientEndPoint = null;
    }
}
