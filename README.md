# run-with-password

This is a simple command line utility that allows you to run processes from the cmd line with out exposing the commands passwords/keys in plain text. 

## Getting Started

Firstly, we need to create a store and a password that you might want to use. To do this we can run the `add-password` command

    rwpass add-password test

We this is ran you will be assked initially for a passphrase. This is the shared secret that is used to encrypt your password store.
Then you will be asked for the actual password you wish to store


    C:\> rwpass add-password test
    Enter encryption passphrase: ********
    Enter password: *******

Now we have a password stored, we can use it. 

As a test lets create a little executable we can run called `test.bat` || `test.sh`

    echo %1

for windows or, 

    echo $1

for Unix, Now we can execute our example program using `rwpass`

    rwpass exe -r test.bat -p:{pw:test}

here we are running our `test.bat` file with a `-p` parameter the value of which will get replaced with the passsword we stored under the `test` key previously. 

    C:\>rwpass exe -r test.bat -p:{pw:test}
    Enter encryption passphrase: ********

outputs

    C:\>echo -p:********
    -p:********

Now you might have expected the password in this case to be echoed back to the screen, which would have defaeated the propose of this tool, but the output is santised before printing. 

