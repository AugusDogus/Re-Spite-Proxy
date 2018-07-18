using RespiteTcpProxy.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Re_Spite_Proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting server...");

            Server server = new Server();
            server.RemoteAddress = "game.re-spite.com";
            server.RemotePort = 8768;

            server.Start();
        
            //When a character is press, stop the server.
            Console.ReadLine();
        }
    }
}
