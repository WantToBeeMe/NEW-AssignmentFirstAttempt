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
    
    private Socket socket;
    private IPEndPoint clientEndPoint;
    private int slowStartThreshold;
    private int currentWindowSize;
    private int nextDataMessageId;
    
    public void start()
    {   //DIRK: for some reason this methods name is uncapitalized, just leave it as is
        //DIRK: any comment not written with DIRK or ISSAM is a note that should be deleted later
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, PORT));
        
        // ASSIGNMENT: The server will always stay online after terminating the operation waiting for a new Hello.
        while (true) { RecieveMessage(); }
    }
    
    //TODO: keep receiving messages from clients
    // you can call a dedicated method to handle each received type of messages
    private void RecieveMessage()
    {
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        byte[] data = new byte[BUFFER_SIZE];
        int bytesReceived = socket.ReceiveFrom(data, ref remoteEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
            );
        if (message == null)
        {
            HandleError("Failed to deserialize message", true);
            return;
        }
        switch (message.Type)
        {
            case MessageType.Hello:
                RecieveHelloMessage(message, remoteEndPoint);
                break;
            case MessageType.RequestData:
                Console.WriteLine("Received RequestData");
                break;
            case MessageType.Ack:
                Console.WriteLine("Received Ack");
                break;
            case MessageType.Error:
                HandleError($"Received Error message from client '{message.Content}'", false);
                return; //DIRK: return serves same purpose as break atm (like the breaks you see above), but if a future dev adds code under the switch, it will also prevent that code from running 
            default:
                HandleError($"Received unexpected message type '{message.Type}' with content '{message.Content}'", true);
                return;
        }
    }

    //TODO: [Receive Hello]
    private void RecieveHelloMessage(Message message, EndPoint remoteEndPoint)
    {
        // DIRK: this might need to be rewored or combined with the RecieveMessage method
        //       its because there can only be 1 client at a time to be comunicated with
        //       so if the message is Hello, it should set the clientEndPoint, and otherwise it should throw an error
        Console.WriteLine("Received Hello message from client.");
        clientEndPoint = (IPEndPoint)remoteEndPoint;
        Console.WriteLine(clientEndPoint);
        if (!int.TryParse(message.Content, out slowStartThreshold))
        {
            HandleError("Failed to parse slow start threshold", true);
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

    //TODO: [Send Data]

    //TODO: [Implement your slow-start algorithm considering the threshold] 

    //TODO: [End sending data to client]

    //TODO: [Handle Errors]
    private void HandleError(string description,  bool notifyClient)
    {
        Console.WriteLine($"Error: {description}");

        if (notifyClient)
        {
            Message errorMessage = new Message { Type = MessageType.Error, Content = description };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(errorMessage));
            socket.SendTo(data, clientEndPoint);
        }

        currentWindowSize = 1;
        nextDataMessageId = 1;
        slowStartThreshold = 0;
        clientEndPoint = null;
    }
}
