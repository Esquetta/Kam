namespace SmartVoiceAgent.Core.Enums;

/// <summary>
/// Represents the types of commands the agent can recognize and execute.
/// </summary>
public enum CommandType
{
    OpenApplication,
    SendMessage,
    PlayMusic,
    SearchWeb,
    ControlDevice,
    CloseApplication,
    AddTask,      
    UpdateTask,   
    DeleteTask,  
    ListTasks,    
    SetReminder  
    Unknown
}
