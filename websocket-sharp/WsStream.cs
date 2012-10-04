#region MIT License
/**
 * WsStream.cs
 *
 * The MIT License
 *
 * Copyright (c) 2010-2012 sta.blockhead
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WebSocketSharp.Frame;
using WebSocketSharp.Net.Security;

namespace WebSocketSharp
{
  public class WsStream : IDisposable
  {
    #region Fields

    private Stream _innerStream;
    private Type   _innerStreamType;
    private bool   _isSecure;
    private Object _forRead;
    private Object _forWrite;

    #endregion

    #region Constructors

    public WsStream(NetworkStream innerStream)
    {
      init(innerStream);
    }

    public WsStream(SslStream innerStream)
    {
      init(innerStream);
    }

    #endregion

    #region Properties

    public bool DataAvailable {
      get {
        if (_innerStreamType == typeof(SslStream))
          return ((SslStream)_innerStream).DataAvailable;

        return ((NetworkStream)_innerStream).DataAvailable;
      }
    }

    public bool IsSecure {
      get { return _isSecure; }
    }

    #endregion

    #region Private Method

    private void init(Stream innerStream)
    {
      if (innerStream == null)
        throw new ArgumentNullException("innerStream");

      _innerStream     = innerStream;
      _innerStreamType = innerStream.GetType();
      _isSecure        = _innerStreamType == typeof(SslStream) ? true : false;
      _forRead         = new object();
      _forWrite        = new object();
    }

    #endregion

    #region Internal Methods

    internal static WsStream CreateClientStream(string hostname, int port, out TcpClient client)
    {
      client = new TcpClient(hostname, port);
      var netStream = client.GetStream();

      if (port == 443)
      {
        System.Net.Security.RemoteCertificateValidationCallback validationCb = (sender, certificate, chain, sslPolicyErrors) =>
        {
          // FIXME: Always returns true
          return true;
        };

        var sslStream = new SslStream(netStream, false, validationCb);
        sslStream.AuthenticateAsClient(hostname);

        return new WsStream(sslStream);
      }

      return new WsStream(netStream);
    }

    internal static WsStream CreateServerStream(TcpClient client)
    {
      var netStream = client.GetStream();

      var port = ((IPEndPoint)client.Client.LocalEndPoint).Port;
      if (port == 443)
      {
        var sslStream = new SslStream(netStream, false);

        var certPath = ConfigurationManager.AppSettings["ServerCertPath"];
        sslStream.AuthenticateAsServer(new X509Certificate2(certPath));

        return new WsStream(sslStream);
      }

      return new WsStream(netStream);
    }

    internal static WsStream CreateServerStream(WebSocketSharp.Net.HttpListenerContext context)
    {
      var conn   = context.Connection;
      var stream = conn.Stream;

      if (conn.IsSecure)
        return new WsStream((SslStream)stream);

      return new WsStream((NetworkStream)stream);
    }

    #endregion

    #region Public Methods

    public void Close()
    {
      _innerStream.Close();
    }

    public void Dispose()
    {
      _innerStream.Dispose();
    }

    public int Read(byte[] buffer, int offset, int size)
    {
      lock (_forRead)
      {
        var readLen = _innerStream.Read(buffer, offset, size);
        if (readLen < size)
        {
          var msg = String.Format("Data can not be read from {0}.", _innerStreamType);
          throw new IOException(msg);
        }
        return readLen;
      }
    }

    public int ReadByte()
    {
      lock (_forRead)
      {
        return _innerStream.ReadByte();
      }
    }

    public WsFrame ReadFrame()
    {
      lock (_forRead)
      {
        return WsFrame.Parse(_innerStream);
      }
    }

    public string[] ReadHandshake()
    {
      lock (_forRead)
      {
        var buffer = new List<byte>();

        while (true)
        {
          if (ReadByte().EqualsAndSaveTo('\r', buffer) &&
              ReadByte().EqualsAndSaveTo('\n', buffer) &&
              ReadByte().EqualsAndSaveTo('\r', buffer) &&
              ReadByte().EqualsAndSaveTo('\n', buffer))
            break;
        }

        return Encoding.UTF8.GetString(buffer.ToArray())
               .Replace("\r\n", "\n").Replace("\n\n", "\n").TrimEnd('\n')
               .Split('\n');
      }
    }

    public void Write(byte[] buffer, int offset, int count)
    {
      lock (_forWrite)
      {
        _innerStream.Write(buffer, offset, count);
      }
    }

    public void WriteByte(byte value)
    {
      lock (_forWrite)
      {
        _innerStream.WriteByte(value);
      }
    }

    public void WriteFrame(WsFrame frame)
    {
      lock (_forWrite)
      {
        var buffer = frame.ToBytes();
        _innerStream.Write(buffer, 0, buffer.Length);
      }
    }

    public void WriteHandshake(Handshake handshake)
    {
      lock (_forWrite)
      {
        var buffer = handshake.ToBytes();
        _innerStream.Write(buffer, 0, buffer.Length);
      }
    }

    #endregion
  }
}
