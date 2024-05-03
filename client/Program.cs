using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MessageNS;

// Students:
// student 1: Dirk Roosendaal - 1031349
// student 2: Issam Ben massoud - 1055156

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
    private const string REQUESTED_FILE = "hamlet.txt";
    private const int PORT = 32000;
    private const string SERVER_IP = "127.0.0.1";
    private const int BUFFER_SIZE = 1024;
    private const int WINDOWSIZE_THRESHOLD = 20;
    private const int DATA_TIMEOUT_MS = 5000;

    private string outputDirectory = AppContext.BaseDirectory;
    private Socket socket;
    private EndPoint serverEndPoint;
    private Dictionary<int, string> receivedMessages;
    private bool receivingData;

    public void start()
    {
        receivingData = true;
        receivedMessages = new Dictionary<int, string>();
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverEndPoint = new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT);
        
        SendHelloMessage();
        ReceiveWelcomeMessage();
        SendRequestDataMessage(REQUESTED_FILE);

       while (receivingData) 
           ReceiveDataMessage();
       EndConnection();
    }

    private void SendMessage(MessageType type, string? content = null)
    {
        Message message = new Message { Type = type, Content = content };
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        socket.SendTo(data, serverEndPoint);
    }
    private Message ReceiveMessage(MessageType expectedType, int timeout = 0)
    {
        // This method handles all the generic receive message logic. when you run this you can assume the following.
        //  1. The message is of the expected type
        //  2. The message has content (if that type should have)
        //  3. The message is not an error message
        // If any of these assumptions are not true, an error will be thrown and the server will reset itself.
        byte[] data = new byte[BUFFER_SIZE];
        socket.ReceiveTimeout = timeout;
        int bytesReceived = socket.ReceiveFrom(data, ref serverEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
            );
  
        if (message == null) 
            HandleError("Failed to deserialize message.", true);
        if(message!.Type == MessageType.Error) 
            HandleError($"Received Error message from server '{message.Content}'", false);
        if (expectedType == MessageType.Data && message.Type == MessageType.End)
            return message; // When we as a client want to receive Data, we can also expect an End message at any point in time
            // therefore this is not an error, and let the caller handle it.
        
        if (message.Type != expectedType) 
            HandleError($"Expected '{expectedType}' message, but received '{message.Type}' from server.", true);

        if (message.Type != MessageType.End && message.Type != MessageType.Welcome)
        { // END and WELCOME are the only 2 message types that dont have content
            if (string.IsNullOrEmpty(message.Content)) 
                HandleError("Received empty message, expected there to be content.", true);
        }
       
        return message;
    }
 
    //TODO: [Send Hello message]
    private void SendHelloMessage()
    {
        SendMessage(MessageType.Hello, WINDOWSIZE_THRESHOLD.ToString());
        Console.WriteLine("Sent Hello message to server.");
    }
    
    //TODO: [Receive Welcome]
    private void ReceiveWelcomeMessage()
    {
        ReceiveMessage(MessageType.Welcome);
        Console.WriteLine("Received Welcome message from server.");
    }
    
    //TODO: [Send RequestData]
    private void SendRequestDataMessage(string fileName)
    {
        SendMessage(MessageType.RequestData, fileName);
        Console.WriteLine("Sent RequestData message to server.");
    }

    private void WriteDataToFile()
        {
            var sortedMessages = receivedMessages.OrderBy(x => x.Key);

            var fileContent = string.Concat(sortedMessages.Select(x => x.Value));

            var filePath = Path.Combine(outputDirectory, REQUESTED_FILE);
            File.WriteAllText(filePath, fileContent);
            
            Console.WriteLine($"File download complete! written to '{filePath}'.");
        }

    //TODO: [Receive Data]
    private void ReceiveDataMessage()
    {
        Message message;
        try {
            message = ReceiveMessage(MessageType.Data, DATA_TIMEOUT_MS);
        } catch (SocketException ex)
        {
            // If the error was not a timeout, we still have to throw it and reset the server.
            if (ex.SocketErrorCode != SocketError.TimedOut)
                HandleError(ex.Message, true);
          
            Console.WriteLine("Timeout, took too long to receive Data message. terminating program safely.");
            receivingData = false;
            return;
        }

        if (message.Type == MessageType.End)
        {
            WriteDataToFile();
            receivingData = false;
            return;
        }
        
        if (message.Content!.Length < 4) 
            HandleError("Received Data message with invalid content.", true);
        if (!int.TryParse(message.Content.Substring(0, 4), out int ackIndex)) 
            HandleError("Received Data message with invalid index.", true);
        string content = message.Content!.Substring(4);

        receivedMessages[ackIndex] = content;
        Console.WriteLine($"Received Data message with index {ackIndex}, sending ack back to server.");
        
        SendMessage(MessageType.Ack, ackIndex.ToString());
    }
    
    private void EndConnection()
    {
        Console.WriteLine("Connection to the server will be ended.");
        socket.Close();
        Environment.Exit(0);
    }
    
    //TODO: [Handle Errors]
    private void HandleError(string description, bool notifyServer)
    {
        Console.WriteLine($"Error: {description}");
        if (notifyServer)
            SendMessage(MessageType.Error, description);
        
        EndConnection();
    }
}
