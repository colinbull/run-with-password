// include Fake libs
#r "./packages/FAKE/tools/FakeLib.dll"

open Fake

// Directories
let buildDir  = "./build/"
let tempDir = "./temp/"


// Filesets
let appReferences  =
    !! "/**/*.csproj"
      ++ "/**/*.fsproj"

// version info
let version = "0.1"  // or retrieve from CI server

// Targets
Target "Clean" (fun _ ->
    CleanDirs [buildDir; tempDir]
)

Target "Build" (fun _ ->
    // compile all projects below src/app/
    MSBuildDebug buildDir "Build" appReferences
        |> Log "AppBuild-Output: "
)

Target "ILRepack" (fun _ ->
    CreateDir tempDir

    let internalizeIn filename = 
        let toPack =
            Seq.append [buildDir </> filename] !!(buildDir </> "*.dll")
            |> separated " "

        trace toPack
        let targetFile = tempDir </> filename

        let result =
            ExecProcess (fun info ->
                info.FileName <- currentDirectory </> "packages" </> "ILRepack" </> "tools" </> "ILRepack.exe"
                info.Arguments <- sprintf "/verbose /lib:%s /ver:%s /out:%s %s" buildDir version targetFile toPack) (System.TimeSpan.FromMinutes 5.)

        if result <> 0 then failwithf "Error during ILRepack execution."

        CopyFile (buildDir </> filename) targetFile

    internalizeIn "rwpass.exe"
    
    !! (buildDir </> "*.*") 
        -- (buildDir </> "*.bin") 
        -- (buildDir </> "*.exe")
        -- (buildDir </> "*.bat")
    |> Seq.iter DeleteFile
    
    DeleteDir tempDir
)

Target "Default" DoNothing

// Build order
"Clean"
  ==> "Build"
  ==> "ILRepack"
  ==> "Default"

// start build
RunTargetOrDefault "Default"
