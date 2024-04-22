using System;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MessageNS;


// Do not modify this class
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
    private const int DATA_CHUNK_SIZE = 100;
    private const int TIMEOUT_MS = 1000;
    
    private Socket socket;
    private IPEndPoint clientEndPoint;
    private int slowStartThreshold;
    private int currentWindowSize;
    private int nextDataMessageId;
    
    // Do not put all the logic into one method. Create multiple methods to handle different tasks.
    public void start()
    {   //DIRK: for some reason this methods name is uncapitalized, just leave it as is
        //DIRK: any comment not written with DIRK or ISSAM as prefix is not written by us and should not be removed
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, PORT));
        
        //DIRK: investigate if we are allowed to use CancellationTokenSource. and even if we should use it
        //      now it just does this forever which is good enough for now, but obviously we should not do this in the real submission
        while (true)
        {
            RecieveClientMessage();
        }
    }

    //TODO: keep receiving messages from clients
    // you can call a dedicated method to handle each received type of messages
    private void RecieveClientMessage()
    {
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        byte[] data = new byte[BUFFER_SIZE];
        int bytesReceived = socket.ReceiveFrom(data, ref remoteEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
            );
        if (message == null)
        {
            Console.WriteLine("Failed to deserialize message.");
            return;
            //DIRK: Investigate what we should do if the message failst to deserialize, aka in this code block
            //      should we then just ignore this message? at least we should let the server know right, or maybe send error back to cleint?
            //      i think any error should be reported to the client right? 
        }
        switch (message.Type)
        {
            case MessageType.Hello:
                HandleHelloMessage(message, remoteEndPoint);
                break;
            case MessageType.RequestData:
                Console.WriteLine("Received RequestData");
                break;
            case MessageType.Ack:
                Console.WriteLine("Received Ack");
                break;
            case MessageType.Error:
                Console.WriteLine("Received Error");
                break;
            default:
                Console.WriteLine("Received unknown message type");
                break;
        }
    }

    //TODO: [Receive Hello]
    private void HandleHelloMessage(Message message, EndPoint remoteEndPoint)
    {
        Console.WriteLine("Received Hello message from client.");
        Console.WriteLine(message);
        clientEndPoint = (IPEndPoint)remoteEndPoint;
        if (!int.TryParse(message.Content, out slowStartThreshold))
        {
            Console.WriteLine("Invalid slow start threshold");
            //DIRK: Investigate what we should do if there is no threshold given, aka in this code block
            //      should we then just ignore this message? or set a default? or something else? 
        }
        currentWindowSize = 1;
        nextDataMessageId = 1;
   
        SendWelcomeMessage();
    }
    //TODO: [Send Welcome]
    private void SendWelcomeMessage()
    {
        Message welcomeMessage = new Message { Type = MessageType.Welcome };
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(welcomeMessage));
        socket.SendTo(data, clientEndPoint);
    }
    
    //TODO: [Receive RequestData]

    //TODO: [Send Data]

    //TODO: [Implement your slow-start algorithm considering the threshold] 

    //TODO: [End sending data to client]

    //TODO: [Handle Errors]

    //TODO: create all needed methods to handle incoming messages
}
