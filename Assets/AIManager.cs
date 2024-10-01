using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OpenAI;
using Samples.Whisper;
using System;
using System.IO; // Para trabalhar com arquivos
using LMNT;

public class AIManager : MonoBehaviour
{
    private OpenAIApi openAI = new OpenAIApi();
    private List<ChatMessage> messages = new List<ChatMessage>();

    [SerializeField] private Transform player;
    [SerializeField] private float interactionDistance = 3f;

    private readonly string fileName = "output.wav";
    private readonly int duration = 120;

    private AudioClip clip;
    private bool isRecording;
    private float time;
    private float recordingDuration;

    private LMNTSpeech speech;
    private AudioSource audioSource;

    private bool hasSentInitialPrompt = false; 

    private string initialPromptPath = "initialPrompt";

    private string csvFilePath = "conversation_log.csv";

    private void Start()
    {
        // Cria o arquivo CSV e adiciona o cabeçalho
        if (!File.Exists(csvFilePath))
        {
            using (StreamWriter writer = new StreamWriter(csvFilePath, true))
            {
                writer.WriteLine("Data,Individuo,Mensagem,Duracao,Tokens");
            }

            Debug.Log($"Created {csvFilePath} file");
        }

        // Carrega o prompt inicial de um arquivo externo
        if (!hasSentInitialPrompt)
        {
            SendInitialPrompt();
        }
    }

    private void Update()
    {
        // Verifica se o jogador está perto o suficiente
        if (Vector3.Distance(player.position, transform.position) <= interactionDistance)
        {
            // Inicia a gravação quando a tecla "F" for pressionada
            if (Input.GetKeyDown(KeyCode.F) && !isRecording)
            {
                StartRecording();
                time = Time.time; // Captura o tempo em que a tecla foi pressionada
                Debug.Log("Pressed F - Recording started");
            }

            // Para a gravação quando a tecla "F" for solta
            if (Input.GetKeyUp(KeyCode.F) && isRecording)
            {
                recordingDuration = Time.time - time; // Calcula o tempo total de gravação
                StopRecording(); // Passa o tempo de gravação para a função
                Debug.Log($"Released F - Recording stopped... Duration: {recordingDuration} seconds");
            }
        }
    }

    private void SendInitialPrompt()
    {
        // Lê o conteúdo do arquivo de texto
        TextAsset promptFile = Resources.Load<TextAsset>(initialPromptPath);

        if (promptFile != null)
        {
            ChatMessage initialMessage = new ChatMessage
            {
                Content = promptFile.text,
                Role = "system"
            };

            messages.Add(initialMessage);
            hasSentInitialPrompt = true; // Marca que o prompt inicial já foi enviado
        }
        else
        {
            Debug.LogError("Initial prompt file not found!");
        }
    }

    private void StartRecording()
    {
        isRecording = true;

        // Usa o microfone padrão do sistema para iniciar a gravação
        clip = Microphone.Start(Microphone.devices[0], false, duration, 44100);
    }

    private void StopRecording()
    {
        isRecording = false;

        // Finaliza a gravação e para o microfone
        Microphone.End(Microphone.devices[0]);

        // Recorta o áudio para o tempo gravado corretamente
        clip = TrimAudioClip(clip);

        EndRecording();
    }

    private AudioClip TrimAudioClip(AudioClip clip)
    {
        int samples = Mathf.FloorToInt(clip.frequency * recordingDuration);
        float[] data = new float[samples];

        // Copia apenas os samples correspondentes ao tempo de gravação
        clip.GetData(data, 0);

        AudioClip trimmedClip = AudioClip.Create(clip.name + "_trimmed", samples, clip.channels, clip.frequency, false);
        trimmedClip.SetData(data, 0);

        return trimmedClip;
    }

    private async void EndRecording()
    {
        Debug.Log("Transcripting...");

        byte[] data = SaveWav.Save(fileName, clip);

        CreateAudioTranscriptionsRequest req = new()
        {
            FileData = new FileData() { Data = data, Name = "audio.wav" },
            Model = "whisper-1",
            Language = "pt"
        };

        var res = await openAI.CreateAudioTranscription(req);

        Debug.Log("Player: " + res.Text);

        AskChatGPTAsync(res.Text);
    }

    public async void AskChatGPTAsync(string transcribedText)
    {
        Debug.Log("Asking...");

        ChatMessage newMessage = new()
        {
            Content = transcribedText,
            Role = "user"
        };

        messages.Add(newMessage);

        CreateChatCompletionRequest request = new()
        {
            Messages = messages,
            Model = "gpt-4o-mini"
        };

        var response = await openAI.CreateChatCompletion(request);

        if (response.Choices != null && response.Choices.Count > 0)
        {
            ChatMessage chatResponse = response.Choices[0].Message;
            messages.Add(chatResponse);

            Debug.Log("Borin: " + chatResponse.Content);

            // Registrar mensagem do jogador com os tokens da mensagem enviada
            LogConversation("Player", transcribedText, response.Usage.PromptTokens);

            // Registrar mensagem do NPC com os tokens da resposta
            LogConversation("NPC", chatResponse.Content, response.Usage.CompletionTokens);

            TextToSpeech(chatResponse.Content);
        }
    }

    public void TextToSpeech(string chatGPTResponse)
    {
        LMNTSpeech speech = GetComponent<LMNTSpeech>();
        speech.language = "pt";
        speech.dialogue = chatGPTResponse;

        StartCoroutine(speech.Talk());
    }

    private void LogConversation(string speaker, string message, string tokens)
    {
        string currentDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        message = message.Replace(",", ";")
                         .Replace("\r\n", " ")
                         .Replace("\n", " ");

        // Escreve a linha no arquivo CSV
        using (StreamWriter writer = new StreamWriter(csvFilePath, true))
        {
            writer.WriteLine($"{currentDate},{speaker},{message},{recordingDuration.ToString().Replace(",", ".")},{tokens}");
        }
    }
}
