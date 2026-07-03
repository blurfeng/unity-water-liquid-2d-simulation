
// 生成随机数的函数。
// Function to generate random numbers.
// 乱数を生成する関数。
// @param uv 输入的二维向量。 Input 2D vector. 入力2Dベクトル。
// @return 返回0-1之间的随机数。 Returns random number between. 0-1 0-1の間の乱数を返す。
float random (float2 uv)
{
    return frac(sin(dot(uv,float2(12.9898,78.233)))*43758.5453123);
}

// 柏林噪声函数，生成平滑的噪声图案。
// Perlin noise function that generates smooth noise patterns.
// 滑らかなノイズパターンを生成するパーリンノイズ関数。
// @param coord 噪声坐标。 Noise coordinates. ノイズ座標。
// @return 返回噪声值。 Returns noise value. ノイズ値を返す。
float noise(float2 coord)
{
    // 获取整数和小数部分。
    // Get integer and fractional parts.
    // 整数部分と小数部分を取得。
    float2 i = floor(coord);
    float2 f = frac(coord);

    // 计算四个角的随机值。
    // Calculate random values for four corners.
    // 四つの角の乱数値を計算。
    float a = random(i);
    float b = random(i + float2(1.0, 0.0));
    float c = random(i + float2(0.0, 1.0));
    float d = random(i + float2(1.0, 1.0));

    // 使用平滑插值函数。
    // Use smooth interpolation function.
    // 滑らかな補間関数を使用。
    float2 cubic = f * f * (3.0 - 2.0 * f);

    // 双线性插值计算最终噪声值。
    // Bilinear interpolation to calculate final noise value.
    // 双線形補間で最終ノイズ値を計算。
    return lerp(a, b, cubic.x) + (c - a) * cubic.y * (1.0 - cubic.x) + (d - b) * cubic.x * cubic.y;
}