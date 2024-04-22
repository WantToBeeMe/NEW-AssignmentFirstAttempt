using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MessageNS;

// SendTo();
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
    private const int TIMEOUT_MS = 1000;
    
    private Socket socket;
    private IPEndPoint serverEndPoint;
    private int slowStartThreshold;
    private int currentWindowSize;
    private int nextExpectedMessageId;
    private Dictionary<int, string> receivedData;
    private List<int> unacknowledgedMessageIds;
    
    // Do not put all the logic into one method. Create multiple methods to handle different tasks.
    public void start()
    {   //DIRK: for some reason this methods name is uncapitalized, just leave it as is
        //DIRK: any comment not written with DIRK or ISSAM as prefix is not written by us and should not be removed
        
        slowStartThreshold = THRESHOLD;
        currentWindowSize = 1;
        nextExpectedMessageId = 1;
        receivedData = new Dictionary<int, string>();
        unacknowledgedMessageIds = new List<int>();
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverEndPoint = new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT);
        
        SendHelloMessage();
        ReceiveWelcomeMessage();
    }

    //TODO: [Send Hello message]
    private void SendHelloMessage()
    {
        Message helloMessage = new Message { Type = MessageType.Hello, Content = slowStartThreshold.ToString() };
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(helloMessage));
        socket.SendTo(data, serverEndPoint);
        Console.WriteLine("Sent Hello message to server.");
    }
    
    //TODO: [Receive Welcome]
    private void ReceiveWelcomeMessage()
    {
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        byte[] data = new byte[BUFFER_SIZE];
        int bytesReceived = socket.ReceiveFrom(data, ref remoteEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
            );
        if (message == null)
        {
            HandleError("Failed to deserialize message.");
            return;
        }
        if (message.Type != MessageType.Welcome)
        {
            HandleError($"Expected Welcome message, but received {message.Type}");
            return;
        }

        Console.WriteLine("Received Welcome message from server.");
    }
    
    
    
    //TODO: [Send RequestData]

    //TODO: [Receive Data]

    //TODO: [Send RequestData]

    //TODO: [Send End]

    //TODO: [Handle Errors]
    private void HandleError(string errorMessage)
    {
        Console.WriteLine($"Error: {errorMessage}");
        // DIRK: this handle error method is not complete!!
    }
}
