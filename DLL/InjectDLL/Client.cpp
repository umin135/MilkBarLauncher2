#pragma once

#pragma comment(lib, "ws2_32")
#include "Connectivity.h"
#include <string>

using namespace Connectivity;

void Client::connectToServer(std::string IP, std::string PORT)
{
    WSAStartup(MAKEWORD(2, 0), &WSAData);
    server = socket(AF_INET, SOCK_STREAM, 0);

    inet_pton(AF_INET, IP.c_str(), &addr.sin_addr);
    addr.sin_family = AF_INET;
    addr.sin_port = htons(std::strtol(PORT.c_str(), nullptr, 10));

    connect(server, (SOCKADDR*)&addr, sizeof(addr));
}

void Client::sendMessage(std::string command, std::string message)
{
    int dataSize = message.size();

    std::string messageToSend = std::to_string(dataSize);

    int numberOfZeros = 5 - messageToSend.size();

    for (int i = 0; i < numberOfZeros; i++)
    {
        messageToSend = "0" + messageToSend;
    }

    messageToSend += command;

    numberOfZeros = 11 - command.size();

    for (int i = 0; i < numberOfZeros; i++)
    {
        messageToSend += "0";
    }

    messageToSend += ";" + message + "END";

    for (int i = 0; i < messageToSend.size(); i++)
    {
        this->buffer[i] = messageToSend[i];
    }

    send(server, buffer, sizeof(buffer), 0);
    memset(buffer, 0, sizeof(buffer));
}

std::string Client::receive()
{

    std::string appendable = "";
    bool CompleteMessage = false;

    while (!CompleteMessage)
    {
        recv(server, buffer, sizeof(buffer), 0);
        //std::string buf = buffer;
        appendable += buffer;
        memset(buffer, 0, sizeof(buffer));

        if (std::count(appendable.begin(), appendable.end(), '{') != std::count(appendable.begin(), appendable.end(), '}') and std::count(appendable.begin(), appendable.end(), '{') > 1)
            continue;

        CompleteMessage = true;
    }

    return appendable;
}

void Client::sendBytes(byte Message[7168])
{
    const char* CharMessage = reinterpret_cast<const char*>(Message);
    send(server, CharMessage, 7168, 0);
}

void Client::receiveBytes(byte* Output)
{
    // patched: read exactly [u16 length][payload]. The original copied `received - 2` bytes, so a
    // closed connection (recv == 0/-1) became a huge memcpy and crashed Cemu; it also assumed the
    // length prefix arrived in the first packet. On failure Output stays zeroed.
    memset(Output, 0, 7168);

    int got = 0;
    while (got < 2)
    {
        int r = recv(server, buffer + got, 2 - got, 0);
        if (r <= 0) return;
        got += r;
    }

    unsigned short msgLength = 0;
    memcpy(&msgLength, &buffer[0], 2);

    int total = 0;
    while (total < msgLength)
    {
        int want = msgLength - total;
        if (total < 7168)
        {
            int r = recv(server, (char*)Output + total, (want < 7168 - total) ? want : 7168 - total, 0);
            if (r <= 0) return;
            total += r;
        }
        else
        {
            int r = recv(server, buffer, (want < 7168) ? want : 7168, 0);  // discard what doesn't fit
            if (r <= 0) return;
            total += r;
        }
    }
}

void Client::close()
{
    closesocket(server);
    WSACleanup();
}