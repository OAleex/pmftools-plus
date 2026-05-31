#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <iostream>
#include <fstream>
#include <iterator>
#include <vector>

#include "cxxopts.hpp"

#include "pmf.h"

using namespace std;

#define READ_BUF_SIZE 1024 * 1024 * 4
#define PMF_VIDEO_START_TICKS 90000L
#define PMF_FRAME_DURATION_TICKS 3003L

int IsMpsFile(std::string *filePath);
int HasAudioPes(std::string *filePath);
void ConfigureVideoOnlyHeader();
long InferVideoDurationTicks(std::string *filePath);
int CountVideoFrames(std::string *filePath);
int CountH264Frames(std::vector<unsigned char> *payload);
int WriteMpsContent(std::string *mpsPath, std::string *pmfPath);
int WritePmfHeader(std::string *pmfPath);
int SetFileSizeInHeader(std::string *mpsPath);
void SetDurationInHeader(long durationTicks);
int FileCombine(std::string *mpsPath, std::string *pmfPath);
long GetFileSize(std::string *fileName);
int convert(std::string *pmfPath, std::string *mpsPath, int mins, int secs, bool makeIcon);

int main(int argc, const char *argv[])
{
    cxxopts::Options options("mps2pmf", "Convert MPS format to PMF format.");
    options
        .positional_help("[optional args]")
        .show_positional_help();

    options.set_width(70)
        .set_tab_expansion()
        .allow_unrecognised_options()
        .add_options()                                                           //
        ("i,mps", "MPS file path", cxxopts::value<std::string>())                //
        ("o,pmf", "PMF file path", cxxopts::value<std::string>())                //
        ("m,min", "Minutes of movie duration", cxxopts::value<int>())            //
        ("s,sec", "Seconds of movie duration", cxxopts::value<int>())            //
        ("c,icon", "Make icon.", cxxopts::value<bool>()->default_value("false")) //
        ("help", "Print help");
    try
    {
        auto result = options.parse(argc, argv);

        if (result.count("help"))
        {
            std::cout << options.help({"", "Group"}) << std::endl;
            exit(0);
        }

        std::string pmfPath = result["pmf"].as<std::string>();
        std::string mpsPath = result["mps"].as<std::string>();
        int min = result["min"].as<int>();
        int sec = result["sec"].as<int>();
        bool makeIcon = result["icon"].as<bool>();

        return convert(&pmfPath, &mpsPath, min, sec, makeIcon);
    }
    catch (const cxxopts::OptionException &e)
    {
        std::cout << "error parsing options: " << e.what() << std::endl;
        std::cout << options.help({"", "Group"}) << std::endl;
        exit(1);
    }
}

int convert(std::string *pmfPath, std::string *mpsPath, int mins, int secs, bool makeIcon)
{
    if (IsMpsFile(mpsPath) == 0)
    {
        std::cout << "Invalid MPS file." << std::endl;
        return 1;
    }

    if (makeIcon)
    {
        HeaderFile[7] = 0x34;
        HeaderFile[83] = 0x3E;
        HeaderFile[104] = 0x01;
        HeaderFile[109] = 0x24;
        HeaderFile[127] = 0x12;
        HeaderFile[129] = 0x01;
        HeaderFile[132] = 0x20;
        HeaderFile[133] = 0x14;
        HeaderFile[142] = 0x09;
        HeaderFile[143] = 0x05;
        HeaderFile[146] = 0x00;
        HeaderFile[148] = 0x00;
        HeaderFile[149] = 0x00;
        HeaderFile[150] = 0x00;
        HeaderFile[151] = 0x00;
        HeaderFile[160] = 0x00;
        HeaderFile[161] = 0x00;
    }

    int hasAudio = HasAudioPes(mpsPath);
    long totalTime = ((mins * 60L) + secs) * 90000L;

    if (hasAudio == 0)
    {
        ConfigureVideoOnlyHeader();

        long videoOnlyDuration = InferVideoDurationTicks(mpsPath);
        if (videoOnlyDuration > 0)
        {
            totalTime = videoOnlyDuration;
        }
    }

    SetDurationInHeader(totalTime);

    if (pmfPath->empty())
    {
        return -1;
    }
    if (SetFileSizeInHeader(mpsPath) == -1)
    {
        return -1;
    }
    if (FileCombine(mpsPath, pmfPath) == -1)
    {
        return -1;
    }

    return 0;
}

void ConfigureVideoOnlyHeader()
{
    HeaderFile[83] = 0x3E;
    HeaderFile[104] = 0x01;
    HeaderFile[109] = 0x24;
    HeaderFile[127] = 0x12;
    HeaderFile[129] = 0x01;

    for (int i = 146; i <= 149; i++)
    {
        HeaderFile[i] = 0x00;
    }

    HeaderFile[160] = 0x00;
    HeaderFile[161] = 0x00;
}

void SetDurationInHeader(long durationTicks)
{
    long tmp = durationTicks >> 24;
    HeaderFile[92] = tmp;
    HeaderFile[118] = tmp;

    tmp = durationTicks >> 16;
    HeaderFile[93] = tmp;
    HeaderFile[119] = tmp;

    tmp = (durationTicks >> 8) & 0xFF;
    HeaderFile[94] = tmp;
    HeaderFile[120] = tmp;

    tmp = durationTicks & 0xFF;
    HeaderFile[95] = tmp;
    HeaderFile[121] = tmp;
}

long InferVideoDurationTicks(std::string *filePath)
{
    int frames = CountVideoFrames(filePath);
    if (frames <= 0)
        return -1;

    return PMF_VIDEO_START_TICKS + (frames * PMF_FRAME_DURATION_TICKS);
}

long GetFileSize(std::string *fileName)
{
    ifstream in(*fileName, ios::binary);
    if (!in.is_open())
    {
        return -1;
    }

    in.seekg(0, ios::end);
    long fileSize = in.tellg();
    in.close();
    return fileSize;
}

int FileCombine(std::string *mpsPath, std::string *pmfPath)
{
    if (WritePmfHeader(pmfPath) == -1)
    {
        std::cout << "Failed." << std::endl;
        return -1;
    }

    int r = WriteMpsContent(mpsPath, pmfPath);
    if (r == -1)
    {
        std::cout << "Failed." << std::endl;
        return -1;
    }

    return 1;
}

int SetFileSizeInHeader(std::string *mpsPath)
{
    long fileSize = GetFileSize(mpsPath);
    if (fileSize == -1)
        return -1;

    HeaderFile[12] = (fileSize >> 24) & 0xFF;
    HeaderFile[13] = (fileSize >> 16) & 0xFF;
    HeaderFile[14] = (fileSize >> 8) & 0xFF;
    HeaderFile[15] = (fileSize >> 0) & 0xFF;
    return 1;
}

int WritePmfHeader(std::string *pmfPath)
{
    ofstream out(*pmfPath, ios::binary);
    if (!out.is_open())
    {
        return -1;
    }

    for (int i = 0; i < 2048; i++)
    {
        out.put(HeaderFile[i]);
    }
    out.close();

    return 1;
}

int WriteMpsContent(std::string *mpsPath, std::string *pmfPath)
{
    long mpsSize = GetFileSize(mpsPath);
    if (mpsSize == -1)
    {
        return -1;
    }

    ifstream mps(*mpsPath, ios::binary);
    if (!mps.is_open())
    {
        std::cout << "Failed to open MPS file." << std::endl;
        return -1;
    }

    ofstream pmf(*pmfPath, ios::app | ios::binary);
    if (!pmf.is_open())
    {
        std::cout << "Failed to open PMF file." << std::endl;
        return -1;
    }

    char *buf = new char[READ_BUF_SIZE];
    while (mpsSize > 0)
    {
        long readSize = mpsSize > READ_BUF_SIZE ? READ_BUF_SIZE : mpsSize;
        mps.read(buf, readSize);
        pmf.write(buf, readSize);
        mpsSize -= readSize;
    }

    mps.close();
    pmf.close();
    delete buf;
    return 0;
}

int HasAudioPes(std::string *filePath)
{
    ifstream in(*filePath, ios::binary);
    if (!in.is_open())
    {
        return -1;
    }

    unsigned char prev[3] = {0, 0, 0};
    unsigned char current;
    while (in.read(reinterpret_cast<char *>(&current), 1))
    {
        if (prev[0] == 0x00 && prev[1] == 0x00 && prev[2] == 0x01 && current == 0xBD)
        {
            in.close();
            return 1;
        }

        prev[0] = prev[1];
        prev[1] = prev[2];
        prev[2] = current;
    }

    in.close();
    return 0;
}

int CountVideoFrames(std::string *filePath)
{
    ifstream in(*filePath, ios::binary);
    if (!in.is_open())
    {
        return -1;
    }

    std::vector<unsigned char> file((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    in.close();

    std::vector<unsigned char> video;
    for (size_t i = 0; i + 9 < file.size();)
    {
        if (file[i] == 0x00 && file[i + 1] == 0x00 && file[i + 2] == 0x01 && file[i + 3] == 0xE0)
        {
            int packetLength = (file[i + 4] << 8) | file[i + 5];
            size_t packetEnd = packetLength > 0 ? i + 6 + packetLength : file.size();
            if (packetEnd > file.size())
                packetEnd = file.size();

            size_t payloadStart = i + 9 + file[i + 8];
            if (payloadStart < packetEnd)
                video.insert(video.end(), file.begin() + payloadStart, file.begin() + packetEnd);

            i = packetEnd;
            continue;
        }

        i++;
    }

    return CountH264Frames(&video);
}

int CountH264Frames(std::vector<unsigned char> *payload)
{
    int frames = 0;
    for (size_t i = 0; i + 4 < payload->size(); i++)
    {
        size_t nalOffset = 0;
        if ((*payload)[i] == 0x00 && (*payload)[i + 1] == 0x00 && (*payload)[i + 2] == 0x01)
        {
            nalOffset = i + 3;
            i += 2;
        }
        else if ((*payload)[i] == 0x00 && (*payload)[i + 1] == 0x00 && (*payload)[i + 2] == 0x00 && (*payload)[i + 3] == 0x01)
        {
            nalOffset = i + 4;
            i += 3;
        }
        else
        {
            continue;
        }

        if (nalOffset >= payload->size())
            continue;

        unsigned char nalType = (*payload)[nalOffset] & 0x1F;
        if (nalType == 1 || nalType == 5)
            frames++;
    }

    return frames;
}

int IsMpsFile(std::string *filePath)
{
    ifstream in(*filePath, ios::binary);
    if (!in.is_open())
    {
        return -1;
    }

    char buf[5];
    in.read(buf, 5);
    in.close();
    if ((buf[0] & 0xFF) == 0x00 && (buf[1] & 0xFF) == 0x00 && (buf[2] & 0xFF) == 0x01 && (buf[3] & 0xFF) == 0xBA && (buf[4] & 0xFF) == 0x44)
    {
        return 1;
    }

    return 0;
}
