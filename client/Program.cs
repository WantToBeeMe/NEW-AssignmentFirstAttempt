using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
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
        ClientUDP cUDP = new ClientUDP();
        cUDP.start();
    }
}

class ClientUDP
{
    //TODO: implement all necessary logic to create sockets and handle incoming messages
    //TODO: create all needed objects for your sockets 
    private const int PORT = 32000; //port of the server
    private const string SERVER_IP = "127.0.0.1";
    private const int THRESHOLD = 20;
    private const int BUFFER_SIZE = 1024;
    //private const int TIMEOUT_MS = 1000;
    
    private Socket socket;
    private EndPoint serverEndPoint;
    private int currentWindowSize;
    private int nextExpectedMessageId;
    
    public void start()
    {   //DIRK: for some reason this methods name is uncapitalized, just leave it as is
        //DIRK: any comment not written with DIRK or ISSAM is a note that should be deleted later
        
        currentWindowSize = 1;
        nextExpectedMessageId = 1;
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverEndPoint = new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT);
        
        SendHelloMessage();
        ReceiveWelcomeMessage();
        SendRequestDataMessage("hamlet.txt");
    }
    
    private Message ReceiveMessage()
    {
        // DIRK: not sure if the endpoint is always the same
        //       anyhow, we for sure need to make some generic method so we can filter out the error messages
        byte[] data = new byte[BUFFER_SIZE];
        int bytesReceived = socket.ReceiveFrom(data, ref serverEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
            );
  
        if (message == null) 
            ThrowError("Failed to deserialize message.", true);
        if(message?.Type == MessageType.Error) 
            ThrowError($"Received Error message from server '{message.Content}'", false);
        
        return message!;
    }
 
    //TODO: [Send Hello message]
    private void SendHelloMessage()
    {
        // ASSIGNMENT-PROTOCOL 1. The client sends a Hello-message to the server,
        //      including the threshold for the slow start in the content section.
        Message helloMessage = new Message { Type = MessageType.Hello, Content = THRESHOLD.ToString() };
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(helloMessage));
        socket.SendTo(data, serverEndPoint);
        Console.WriteLine("Sent Hello message to server.");
    }
    
    //TODO: [Receive Welcome]
    private void ReceiveWelcomeMessage()
    {
        Message message = ReceiveMessage();
        if (message.Type != MessageType.Welcome) 
            ThrowError($"Expected Welcome message, but received {message.Type}", true);
        
        Console.WriteLine("Received Welcome message from server.");
    }
    
    //TODO: [Send RequestData]
    private void SendRequestDataMessage(string fileName)
    {
        // ASSIGNMENT-PROTOCOL 3. The client sends a RequestData-message
        Message requestDataMessage = new Message { Type = MessageType.RequestData, Content = fileName };
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(requestDataMessage));
        socket.SendTo(data, serverEndPoint);
        Console.WriteLine("Sent RequestData message to server.");
    }
    
    
    //TODO: [Receive Data]

    //TODO: [Send RequestData]

    //TODO: [Send End]
    
    //TODO: [Handle Errors]
    /* ASSIGNMENT: The Error message is there to communicate to either of the other party that there was an error,
         please be specific about what is the error in the content of the message. Upon reception of an
         error, the server will reset the communication (and be ready again). The client will terminate
         printing the error */
    private void ThrowError(string description, bool notifyServer)
    {
        Console.WriteLine($"Error: {description}");

        if (notifyServer)
        {
            Message errorMessage = new Message { Type = MessageType.Error, Content = description };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(errorMessage));
            socket.SendTo(data, serverEndPoint);
        }
        
        socket.Close();
        Environment.Exit(0);
    }
}
