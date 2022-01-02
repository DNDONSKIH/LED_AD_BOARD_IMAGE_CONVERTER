using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Drawing;
using System.Xml.Serialization;
using System.IO;
using System.Diagnostics;

namespace LED_AD_BOARD_IMAGE_CONVERTER
{
    class Program
    {
        static void Main(string[] args)
        {
            string img_path = "", options_path = "", output_path = "";
            int frame_delay = 0, frame_num = 0;

            //считываем параметры командной строки
            for (int i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--i":
                        img_path = args[++i];
                        break;
                    case "--o":
                        output_path = args[++i];
                        break;
                    case "--s":
                        options_path = args[++i];
                        break;
                    case "--d":
                        frame_delay = Convert.ToInt32(args[++i]);
                        break;
                    case "--n":
                        frame_num = Convert.ToInt32(args[++i]);
                        break;
                    default:
                        break;
                }
            }

            Debug.Assert(File.Exists(img_path));
            Debug.Assert(File.Exists(options_path));
            Debug.Assert(output_path != "");
            Debug.Assert(frame_delay != 0);

            //считываем параметры экрана
            Board_Settings bs = new Board_Settings();

            using (FileStream fs = new FileStream(options_path, FileMode.OpenOrCreate)) {
                XmlSerializer formatter = new XmlSerializer(typeof(Board_Settings));
                bs = (Board_Settings)formatter.Deserialize(fs);
            }

            int lines_c = bs.lines_count;               //количество линий экрана
            int max_line_len = bs.lines_length.Max();   //максимальная длина линии

            PixCoord[,] px_coord = new PixCoord[max_line_len, lines_c];
            //массив px_coord хранит координаты пикселя на обрабатываемом изображении в зависимости от:
            //1-е измерение - номер пикселя в линии, 
            //2-е измерение - номер линии

            foreach (var px in bs.Pixels){
                if (px.line_num >= 0)
                    px_coord[px.num_in_line, px.line_num] = new PixCoord(px.x, px.y);
            }

            //грузим картинку
            Bitmap bmp = new Bitmap(img_path);
            Debug.Assert( (bmp.Width == bs.Board_Width) && (bmp.Height == bs.Board_Heigth) );

            //массив под бинарные данные кадра
            List<ushort> stm_file = new List<ushort>();

            for (int N = 0; N < max_line_len; N++)
            {
                ushort[,,] GRB = new ushort[3, 8, 8];
                //массив GRB содержит коды цветов для набора пикселей в каждой из лент под номерами от 0 до max_line_len
                //1-е измерение - цвет(GRB), 
                //2-е измерение - зависит от номера ленты (максимум 128/16=8), 
                //3-е измерение - номер бита для каждой из составлющих цвета

                for (int L = 0; L < lines_c; L++)
                {
                    int img_x_coord = px_coord[N, L].x;
                    int img_y_coord = px_coord[N, L].y;
                    //отзеркаливаем координату Y
                    img_y_coord = bmp.Height - 1 - img_y_coord;

                    Color curr_px_color = bmp.GetPixel(img_x_coord, img_y_coord);
                    byte[] curr_px_GRB = new byte[3] { curr_px_color.G, curr_px_color.R, curr_px_color.B };

                    int i = L / 16;
                    int bit_pos = L % 16;
                    ushort mask1 = (ushort)(0x1 << bit_pos);

                    for (int j = 0; j < 3; j++){
                        if ((curr_px_GRB[j] & 0x01) != 0) GRB[j,i,0] |= mask1;
                        if ((curr_px_GRB[j] & 0x02) != 0) GRB[j,i,1] |= mask1;
                        if ((curr_px_GRB[j] & 0x04) != 0) GRB[j,i,2] |= mask1;
                        if ((curr_px_GRB[j] & 0x08) != 0) GRB[j,i,3] |= mask1;
                        if ((curr_px_GRB[j] & 0x10) != 0) GRB[j,i,4] |= mask1;
                        if ((curr_px_GRB[j] & 0x20) != 0) GRB[j,i,5] |= mask1;
                        if ((curr_px_GRB[j] & 0x40) != 0) GRB[j,i,6] |= mask1;
                        if ((curr_px_GRB[j] & 0x80) != 0) GRB[j,i,7] |= mask1;
                    }
                }

                //сохраняем часть кадра в формат stm файла
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 8; k++)
                        for (int i = (lines_c - 1) / 16; i >= 0; i--)
                            stm_file.Add( GRB[j, i, k] );
            }

            //создаем массив под выходные данные
            long size = 0;
            bool output_not_empty = File.Exists(output_path);

            if (output_not_empty){
                System.IO.FileInfo file = new System.IO.FileInfo(output_path);
                size = file.Length;
            }

            byte[] output_bin = new byte[size + 16 + (stm_file.Count * 2)]; //размер исходного файла + 16 байт заголовка кадра + данные кадра
            int ii = 0;

            if (output_not_empty) {
                //считываем что уже есть в выходном  файле
                using (BinaryReader reader = new BinaryReader(File.Open(output_path, FileMode.Open)))
                {
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        byte[] tmp = reader.ReadBytes(64 * 1024);
                        for (int i = 0; i < tmp.Length; i++)
                            output_bin[ii++] = tmp[i];
                    }
                }
            }

            // добавляем заголовок кадра
            byte[] fn_bytes = BitConverter.GetBytes((UInt32)frame_num); // номер кадра
            output_bin[ii++] = fn_bytes[0];
            output_bin[ii++] = fn_bytes[1];
            output_bin[ii++] = fn_bytes[2];
            output_bin[ii++] = fn_bytes[3];

            byte[] fd_bytes = BitConverter.GetBytes((UInt32)frame_delay);// длительность отображения кадра
            output_bin[ii++] = fd_bytes[0];
            output_bin[ii++] = fd_bytes[1];
            output_bin[ii++] = fd_bytes[2];
            output_bin[ii++] = fd_bytes[3];

            ii += 8;    // 8 резервных байт

            // добавляем данные кадра
            for (int i = 0; i < stm_file.Count; i++){
                byte[] bytes = BitConverter.GetBytes(stm_file[i]);
                output_bin[ii++] = bytes[0];
                output_bin[ii++] = bytes[1];
            }

            // сохранить результат
            File.WriteAllBytes(output_path, output_bin);
        }
    }

    // for deserializer
    public class Board_Settings
    {
        public int Board_Width;
        public int Board_Heigth;
        public int lines_count;
        public int[] lines_length;

        public Pix[] Pixels;

        public Board_Settings()
        {
        }
    }

    public class Pix
    {
        public int x;
        public int y;
        public int line_num;
        public int num_in_line;
    }

    public class PixCoord
    {
        public int x;
        public int y;

        public PixCoord(int _x, int _y)
        {
            x = _x;
            y = _y;
        }
    }
}
