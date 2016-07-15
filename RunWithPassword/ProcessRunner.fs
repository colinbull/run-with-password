module ProcessRunner

module Encryption =

    open System
    open System.Text
    open System.IO
    open System.Security.Cryptography

    let private iterations = 1000;

    let generate256BitsOfRandomEntropy() =
        let randomBytes = Array.zeroCreate<byte> 32
        use rngCsp = new RNGCryptoServiceProvider()
        rngCsp.GetBytes(randomBytes);
        randomBytes

    let encrypt (passPhrase:string) (plainText:byte[])  = 
        let saltStringBytes = generate256BitsOfRandomEntropy()
        let ivStringBytes = generate256BitsOfRandomEntropy()
        let plainTextBytes = plainText
        use password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, iterations)
        let keyBytes = password.GetBytes(32)
        use symmetricKey = new RijndaelManaged(BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7)
        use encryptor = symmetricKey.CreateEncryptor(keyBytes, ivStringBytes)
        use memoryStream = new MemoryStream()
        use cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write)
        cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length)
        cryptoStream.FlushFinalBlock()
        let cipherTextBytes = 
            Array.append 
                saltStringBytes 
                (Array.append ivStringBytes (memoryStream.ToArray()))
        memoryStream.Close()
        cryptoStream.Close()
        cipherTextBytes
        
    let decrypt (passPhrase:string) (cipherBytes:byte[]) = 
        let cipherTextBytesWithSaltAndIv = cipherBytes;
        let saltStringBytes = cipherTextBytesWithSaltAndIv.[0..31] 
        let ivStringBytes = cipherTextBytesWithSaltAndIv.[32..63]
        let cipherTextBytes = cipherTextBytesWithSaltAndIv.[64..]
        use password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, iterations)
        let keyBytes = password.GetBytes(32);
        use symmetricKey = new RijndaelManaged(BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7)
        use decryptor = symmetricKey.CreateDecryptor(keyBytes, ivStringBytes)
        use memoryStream = new MemoryStream(cipherTextBytes)
        use cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read)
        let plainTextBytes = Array.zeroCreate<byte> cipherTextBytes.Length
        let decryptedByteCount =  ref (cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length))

        memoryStream.Close()
        cryptoStream.Close()
        plainTextBytes

module Password = 

    open System
    open System.IO
    open System.Text
    open System.Security
    open System.Runtime.Serialization.Formatters.Binary
    open Nessos.FsPickler

    type File = Map<string, string>

    let private serialiser = FsPickler.CreateBinarySerializer() 
    let passwordFile = "passdb.bin"

    let tryFindKey key file = 
        file |> Map.tryFind key

    let read passPhrase path =
        let fileBytes =  File.ReadAllBytes(path)
        if fileBytes.Length > 0
        then
            let decrypted = Encryption.decrypt passPhrase fileBytes 
            serialiser.UnPickle<File>(decrypted)
        else 
            Map.empty 

    let write passPhrase path (file:File) =
        let bytes = serialiser.Pickle(file)
        let encrypted = Encryption.encrypt passPhrase bytes
        File.WriteAllBytes(path, encrypted)

    let get passPhrase path key =
        read passPhrase path
        |> tryFindKey key
    
    let addOrUpdate passPhrase path key password = 
        let file = read passPhrase path
        Map.add key password file
        |> write passPhrase path

    let remove path passPhrase key = 
        let file = read passPhrase path
        Map.remove key file
        |> write passPhrase path

    let readPasswordFromConsole (message : string) = 
        Console.Write message
        let mutable continueLooping = true
        let mutable password = ""
        while continueLooping do
            let key = Console.ReadKey true
            continueLooping <- key.Key <> ConsoleKey.Enter
            if continueLooping then 
                password <- 
                    if key.Key <> ConsoleKey.Backspace then 
                        Console.Write "*"
                        password + key.KeyChar.ToString()
                    else if password.Length > 0 then 
                        Console.Write "\b \b"
                        password.Substring(0, (password.Length - 1))
                    else ""
            else Console.WriteLine()
        password
    
    let getPasswordStorePath () =
        let path = Path.GetFullPath("passdb.bin")
        if not(File.Exists path)
        then
            Console.Write (sprintf "No password store found at %s one will be created" path)
            File.Create(path) |> ignore
            path
        else
            path

module ProcessExecutor = 

    open System.Diagnostics
    open System.Text.RegularExpressions

    let private keyPattern = "\{pw:?(\w+)\}"
    let private replacePattern = "\{pw:\w+\}"

    let injectPassword passPhrase path (cmd:string) (args:string) = 
        let m = Regex.Match(args, keyPattern)
        let key = m.Groups.[1].Value
        match Password.get passPhrase path key with
        | Some password ->
            let password = 
                if password |> Seq.exists (fun c -> c = ',' || c = ';' || c = '-' || c = '/' || c = '=' || c = ' ' || c ='\\')
                then sprintf "\"%s\"" password
                else password
            cmd, Regex.Replace(args, replacePattern, password), password
        | None -> failwithf "Unable to find password for key %A" key

    let execute (cmd:string) (args:string) (password:string) =
        let writeSanitized (ev:DataReceivedEventArgs) = 
            if ev <> null && not(System.String.IsNullOrWhiteSpace(ev.Data))
            then
                ev.Data.Replace(password, String.replicate 8 "*") 
                |> System.Console.WriteLine
        let startInfo = 
            let si = ProcessStartInfo(cmd, args)
            si.UseShellExecute <- false
            si.RedirectStandardError <- true
            si.RedirectStandardOutput <- true
            si
        use p = new Process()
        p.StartInfo <- startInfo
        p.ErrorDataReceived |> Event.add (writeSanitized)
        p.OutputDataReceived |> Event.add (writeSanitized)
        p.Start() |> ignore
        p.BeginErrorReadLine()
        p.BeginOutputReadLine() 
        
        p.WaitForExit()
        p.ExitCode

open Argu


type ExecuteArgs =
    | [<MainCommand; ExactlyOnce; Last>] Args of string
    | [<Mandatory; AltCommandLine("-r")>] Process of string
    interface IArgParserTemplate with
        member this.Usage = 
            match this with
            | Process _ -> "The cmd to execute."
            | Args _ -> "The argument to send to the comands"

type AddPasswordArgs =
    | [<MainCommand; ExactlyOnce; Last; Mandatory>] Key of string
    interface IArgParserTemplate with
        member this.Usage = 
            match this with
            | Key _ -> "The key to identify the password."

type RemovePasswordArgs =
    | [<MainCommand; ExactlyOnce; Last; Mandatory>] Key of string
    interface IArgParserTemplate with
        member this.Usage = 
            match this with
            | Key _ -> "The key to remove the password."

[<RequireSubcommand>]
type CLIArguments = 
    | [<CustomCommandLine("exe")>] Exe of ParseResults<ExecuteArgs>
    | [<CustomCommandLine("add-password")>] AddPassword of ParseResults<AddPasswordArgs>
    | [<CustomCommandLine("remove-password")>] RemovePassword of  ParseResults<RemovePasswordArgs>
    | [<AltCommandLine("-p")>] Passphrase of string
    interface IArgParserTemplate with
        member this.Usage = 
            match this with
            | Exe _ -> "A command to execute splicing in passwords where required."
            | AddPassword _ -> "Saves a password in the password store."
            | RemovePassword _ -> "Removes a password for use in an executable."
            | Passphrase _ -> "The shared secret to use for encryption. If this is not provided you will be prompted" 

let parser = ArgumentParser.Create<CLIArguments>(programName = "rwpass.exe", errorHandler = new ProcessExiter())

[<EntryPoint>]
let main argv =
    let args = parser.ParseCommandLine(argv)
    let encryptionPassphrase =
        match args.TryGetResult <@ Passphrase @> with
        | Some phrase -> phrase
        | None -> Password.readPasswordFromConsole "Enter encryption passphrase: "
    let storePath = Password.getPasswordStorePath ()

    match args.GetSubCommand() with
    | Exe args -> 
        let cmd = args.GetResult <@ Process @>
        let args = args.GetResult <@ Args @> 
        ProcessExecutor.injectPassword encryptionPassphrase storePath cmd args
        |||> ProcessExecutor.execute
    | AddPassword args ->
        let key = args.GetResult <@ AddPasswordArgs.Key @>
        let password = Password.readPasswordFromConsole "Enter password: "
        Password.addOrUpdate encryptionPassphrase storePath key password
        0
    | RemovePassword args ->
        let key = args.GetResult <@ RemovePasswordArgs.Key @>
        Password.remove encryptionPassphrase storePath key
        0
    //To maintain compiler warnings
    | Passphrase _ -> failwith "Internal Error: This code should never be reached"

    
    