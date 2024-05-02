using System.ComponentModel;
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
    private const int PORT = 32000; //port of the server
    private const string SERVER_IP = "127.0.0.1";
    private const int BUFFER_SIZE = 1024;
    private const int WINDOWSIZE_THRESHOLD = 20;
    //private const int TIMEOUT_MS = 1000;
    
    private Socket socket;
    private EndPoint serverEndPoint;
    private Dictionary<int, string> receivedMessages;
    private bool receivingData;
    
    public void start()
    {   //DIRK: for some reason this methods name is uncapitalized, just leave it as is
        //DIRK: any comment not written with DIRK or ISSAM is a note that should be deleted later
        
        receivingData = true;
        receivedMessages = new Dictionary<int, string>();
        
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverEndPoint = new IPEndPoint(IPAddress.Parse(SERVER_IP), PORT);
        
        SendHelloMessage();
        ReceiveWelcomeMessage();
        SendRequestDataMessage("hamlet.txt");

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
    private Message ReceiveMessage(MessageType expectedType)
    {
        byte[] data = new byte[BUFFER_SIZE];
        int bytesReceived = socket.ReceiveFrom(data, ref serverEndPoint);
        Message? message = JsonSerializer.Deserialize<Message>(
            Encoding.UTF8.GetString(data, 0, bytesReceived)
            );
  
        if (message == null) 
            ThrowError("Failed to deserialize message.", true);
        if(message!.Type == MessageType.Error) 
            ThrowError($"Received Error message from server '{message.Content}'", false);
        if (expectedType == MessageType.Data && message.Type == MessageType.End)
            return message; // When we as a client want to receive Data, we can also expect an End message at any point in time
        
        if (message.Type != expectedType) 
            ThrowError($"Expected {expectedType} message, but received {message.Type}", true);

        if (message.Type != MessageType.End && message.Type != MessageType.Welcome)
        { // END and WELCOME are the only 2 message types that dont have content
            if (string.IsNullOrEmpty(message.Content)) 
                ThrowError("Received empty message, expected there to be content", true);
        }
       
        return message;
    }
 
    //TODO: [Send Hello message]
    private void SendHelloMessage()
    {
        // ASSIGNMENT-PROTOCOL 1. The client sends a Hello-message to the server,
        //      including the threshold for the slow start in the content section.
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
        // ASSIGNMENT-PROTOCOL 3. The client sends a RequestData-message
        SendMessage(MessageType.RequestData, fileName);
        Console.WriteLine("Sent RequestData message to server.");
    }

    private void WriteDataToFile()
        {
            // Sort the received messages
            var sortedMessages = receivedMessages.OrderBy(x => x.Key);

            // Combine the message contents into a single string
            var fileContent = string.Concat(sortedMessages.Select(x => x.Value));

            // Write the file content to a file in the project directory
            var filePath = Path.Combine(AppContext.BaseDirectory, "hamlet_received.txt");
            File.WriteAllText(filePath, fileContent);

            Console.WriteLine($"File content written to '{filePath}'");
        }
    
    
    //TODO: [Receive Data]
    private void ReceiveDataMessage()
    {
        Message message = ReceiveMessage(MessageType.Data);
        if (message.Type == MessageType.End)
        {
            WriteDataToFile();
            receivingData = false;
            return;
        }
        
        if (message.Content!.Length < 4) 
            ThrowError("Received Data message with invalid content", true);
        if (!int.TryParse(message.Content.Substring(0, 4), out int ackIndex)) 
            ThrowError("Received Data message with invalid index", true);
        string content = message.Content!.Substring(4);

        // receiving the same ack should be possible (otherwise its quite impossible to resend window)
        //if (receivedMessages.ContainsKey(ackIndex))
        //    ThrowError("Received Data that was already send", true);

        receivedMessages[ackIndex] = content;
        Console.WriteLine($"Received Data message with index {ackIndex}");
        SendMessage(MessageType.Ack, ackIndex.ToString());
    }
    
    private void EndConnection()
    {
        Console.WriteLine("Connection to the server will be ended, file download complete!");
        socket.Close();
        Environment.Exit(0);
    }
    
    //TODO: [Handle Errors]
    /* ASSIGNMENT: The Error message is there to communicate to either of the other party that there was an error,
         please be specific about what is the error in the content of the message. Upon reception of an
         error, the server will reset the communication (and be ready again). The client will terminate
         printing the error */
    private void ThrowError(string description, bool notifyServer)
    {
        Console.WriteLine($"Error: {description}");
        if (notifyServer)
            SendMessage(MessageType.Error, description);
        
        socket.Close();
        Environment.Exit(0);
    }
}
