---
license: apache-2.0
tags:
- diffusion-single-file
- comfyui
- distillation
- LoRA
- video
- video genration
- lora
pipeline_tags:
- image-to-video
- text-to-video
base_model:
- Wan-AI/Wan2.2-I2V-A14B
library_name: diffusers
pipeline_tag: image-to-video
---

# 🎬 Wan2.2 Distilled LoRA Models

### ⚡ High-Performance Video Generation with 4-Step Inference Using LoRA

*LoRA weights extracted from Wan2.2 distilled models - Flexible deployment with excellent generation quality*

![img_lightx2v](https://cdn-uploads.huggingface.co/production/uploads/680de13385293771bc57400b/tTnp8-ARpj3wGxfo5P55c.png)

---

[![🤗 HuggingFace](https://img.shields.io/badge/🤗-HuggingFace-yellow)](https://huggingface.co/lightx2v/Wan2.2-Distill-Loras)
[![GitHub](https://img.shields.io/badge/GitHub-LightX2V-blue?logo=github)](https://github.com/ModelTC/LightX2V)
[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](LICENSE)

---

## 🌟 What's Special?

<table>
<tr>
<td width="50%">

### ⚡ Flexible Deployment
- **Base Model + LoRA**: Can be combined with base models
- **Offline Merging**: Pre-merge LoRA into models
- **Online Loading**: Dynamically load LoRA during inference
- **Multiple Frameworks**: Supports LightX2V and ComfyUI

</td>
<td width="50%">

### 🎯 Dual Noise Control
- **High Noise**: More creative, diverse outputs
- **Low Noise**: More faithful to input, stable outputs
- Rank 64 LoRA, compact size

</td>
</tr>
<tr>
<td width="50%">

### 💾 Storage Efficient
- **Small LoRA Size**: Significantly smaller than full models
- **Flexible Combination**: Can be combined with quantization
- **Easy Sharing**: Convenient for model weight distribution

</td>
<td width="50%">

### 🚀 4-Step Inference
- **Ultra-Fast Generation**: Generate high-quality videos in just 4 steps
- **Distillation Acceleration**: Inherits advantages of distilled models
- **Quality Assurance**: Maintains excellent generation quality

</td>
</tr>
</table>

---

## 📦 LoRA Model Catalog

### 🎥 Available LoRA Models

| Task Type | Noise Level | Model File | Rank | Purpose |
|:-------:|:--------:|:---------|:----:|:-----|
| **I2V** | High Noise | `wan2.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step_xxx.safetensors` | 64 | More creative image-to-video |
| **I2V** | Low Noise | `wan2.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step_xxx.safetensors` | 64 | More stable image-to-video |

> 💡 **Note**: 
> - `xxx` in filenames represents version number or timestamp, please check [HuggingFace repository](https://huggingface.co/lightx2v/Wan2.2-Distill-Loras/tree/main) for the latest version
> - These LoRAs must be used with Wan2.2 base models

---

## 🚀 Usage

### Prerequisites

**Base Model**: You need to prepare Wan2.2 I2V base model (original model without distillation)

Download base model (choose one):

**Method 1: From LightX2V Official Repository (Recommended)**
```bash
# Download high noise base model
huggingface-cli download lightx2v/Wan2.2-Official-Models \
    wan2.2_i2v_A14b_high_noise_lightx2v.safetensors \
    --local-dir ./models/Wan2.2-Official-Models

# Download low noise base model
huggingface-cli download lightx2v/Wan2.2-Official-Models \
    wan2.2_i2v_A14b_low_noise_lightx2v.safetensors \
    --local-dir ./models/Wan2.2-Official-Models
```

**Method 2: From Wan-AI Official Repository**
```bash
huggingface-cli download Wan-AI/Wan2.2-I2V-A14B \
    --local-dir ./models/Wan2.2-I2V-A14B
```

> 💡 **Note**: [lightx2v/Wan2.2-Official-Models](https://huggingface.co/lightx2v/Wan2.2-Official-Models) provides separate high noise and low noise base models, download as needed

### Method 1: LightX2V - Offline LoRA Merging (Recommended ⭐)

**Offline LoRA merging provides best performance and supports quantization simultaneously.**

#### 1.1 Download LoRA Models

```bash
# Download both LoRAs (high noise and low noise)
# Note: xxx represents version number, please check HuggingFace for actual filename
huggingface-cli download lightx2v/Wan2.2-Distill-Loras \
    wan2.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    wan2.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --local-dir ./loras/
```

#### 1.2 Merge LoRA (Basic Merging)

**Merge LoRA:**
```bash
cd LightX2V/tools/convert

# For directory-based base model: --source /path/to/Wan2.2-I2V-A14B/high_noise_model/
python converter.py \
    --source ./models/Wan2.2-Official-Models/wan2.2_i2v_A14b_high_noise_lightx2v.safetensors \
    --output /path/to/output/ \
    --output_ext .safetensors \
    --output_name wan2.2_i2v_A14b_high_noise_lightx2v_4step \
    --model_type wan_dit \
    --lora_path /path/to/loras/wan2.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --lora_strength 1.0 \
    --single_file

# For directory-based base model: --source /path/to/Wan2.2-I2V-A14B/low_noise_model/
python converter.py \
    --source ./models/Wan2.2-Official-Models/wan2.2_i2v_A14b_low_noise_lightx2v.safetensors \
    --output /path/to/output/ \
    --output_ext .safetensors \
    --output_name wan2.2_i2v_A14b_low_noise_lightx2v_4step \
    --model_type wan_dit \
    --lora_path /path/to/loras/wan2.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --lora_strength 1.0 \
    --single_file
```

#### 1.3 Merge LoRA + Quantization (Recommended)

**Merge LoRA + FP8 Quantization:**
```bash
cd LightX2V/tools/convert

# For directory-based base model: --source /path/to/Wan2.2-I2V-A14B/high_noise_model/
python converter.py \
    --source ./models/Wan2.2-Official-Models/wan2.2_i2v_A14b_high_noise_lightx2v.safetensors \
    --output /path/to/output/ \
    --output_ext .safetensors \
    --output_name wan2.2_i2v_A14b_high_noise_scaled_fp8_e4m3_lightx2v_4step \
    --model_type wan_dit \
    --lora_path /path/to/loras/wan2.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --lora_strength 1.0 \
    --quantized \
    --linear_dtype torch.float8_e4m3fn \
    --non_linear_dtype torch.bfloat16 \
    --single_file

# For directory-based base model: --source /path/to/Wan2.2-I2V-A14B/low_noise_model/
python converter.py \
    --source ./models/Wan2.2-Official-Models/wan2.2_i2v_A14b_low_noise_lightx2v.safetensors \
    --output /path/to/output/ \
    --output_ext .safetensors \
    --output_name wan2.2_i2v_A14b_low_noise_scaled_fp8_e4m3_lightx2v_4step \
    --model_type wan_dit \
    --lora_path /path/to/loras/wan2.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --lora_strength 1.0 \
    --quantized \
    --linear_dtype torch.float8_e4m3fn \
    --non_linear_dtype torch.bfloat16 \
    --single_file
```

**Merge LoRA + ComfyUI FP8 Format:**
```bash
cd LightX2V/tools/convert

# For directory-based base model: --source /path/to/Wan2.2-I2V-A14B/high_noise_model/
python converter.py \
    --source ./models/Wan2.2-Official-Models/wan2.2_i2v_A14b_high_noise_lightx2v.safetensors \
    --output /path/to/output/ \
    --output_ext .safetensors \
    --output_name wan2.2_i2v_A14b_high_noise_scaled_fp8_e4m3_lightx2v_4step_comfyui \
    --model_type wan_dit \
    --lora_path /path/to/loras/wan2.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --lora_strength 1.0 \
    --quantized \
    --linear_dtype torch.float8_e4m3fn \
    --non_linear_dtype torch.bfloat16 \
    --single_file \
    --comfyui_mode

# For directory-based base model: --source /path/to/Wan2.2-I2V-A14B/low_noise_model/
python converter.py \
    --source ./models/Wan2.2-Official-Models/wan2.2_i2v_A14b_low_noise_lightx2v.safetensors \
    --output /path/to/output/ \
    --output_ext .safetensors \
    --output_name wan2.2_i2v_A14b_low_noise_scaled_fp8_e4m3_lightx2v_4step_comfyui \
    --model_type wan_dit \
    --lora_path /path/to/loras/wan2.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --lora_strength 1.0 \
    --quantized \
    --linear_dtype torch.float8_e4m3fn \
    --non_linear_dtype torch.bfloat16 \
    --single_file \
    --comfyui_mode
```

> 📝 **Reference Documentation**: For more merging options, see [LightX2V Model Conversion Documentation](https://github.com/ModelTC/LightX2V/blob/main/tools/convert/readme_zh.md)

---

### Method 2: LightX2V - Online LoRA Loading

**Online LoRA loading requires no pre-merging, loads dynamically during inference, more flexible.**

#### 2.1 Download LoRA Models

```bash
# Download both LoRAs (high noise and low noise)
# Note: xxx represents version number, please check HuggingFace for actual filename
huggingface-cli download lightx2v/Wan2.2-Distill-Loras \
    wan2.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    wan2.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step_xxx.safetensors \
    --local-dir ./loras/
```

#### 2.2 Use Configuration File

Reference configuration file: [wan_moe_i2v_distil_with_lora.json](https://github.com/ModelTC/LightX2V/blob/main/configs/wan22/wan_moe_i2v_distil_with_lora.json)

LoRA configuration example in config file:
```json
{
    "lora_configs": [
        {
            "name": "high_noise_model",
            "path": "/path/to/loras/wan2.2_i2v_A14b_high_noise_lora_rank64_lightx2v_4step_xxx.safetensors",
            "strength": 1.0
        },
        {
            "name": "low_noise_model",
            "path": "/path/to/loras/wan2.2_i2v_A14b_low_noise_lora_rank64_lightx2v_4step_xxx.safetensors",
            "strength": 1.0
        }
    ]
}
```

> 💡 **Tip**: Replace `xxx` with actual version number (e.g., `1022`). Check [HuggingFace repository](https://huggingface.co/lightx2v/Wan2.2-Distill-Loras/tree/main) for the latest version


#### 2.3 Run Inference

Using [I2V](https://github.com/ModelTC/LightX2V/blob/main/scripts/wan22/run_wan22_moe_i2v_distill.sh) as example:
```bash
cd scripts
bash wan22/run_wan22_moe_i2v_distill.sh
```

### Method 3: ComfyUI

Please refer to [workflow](https://huggingface.co/lightx2v/Wan2.2-Distill-Loras/blob/main/wan2.2_i2v_scale_fp8_comfyui_with_lora.json)

## ⚠️ Important Notes

1. **Base Model Requirement**: These LoRAs must be used with Wan2.2-I2V-A14B base model, cannot be used standalone

2. **Other Components**: In addition to DIT model and LoRA, the following are also required at runtime:
   - T5 text encoder
   - CLIP vision encoder
   - VAE encoder/decoder
   - Tokenizer
   
   Please refer to [LightX2V Documentation](https://lightx2v-zhcn.readthedocs.io/zh-cn/latest/getting_started/model_structure.html) for how to organize complete model directory

3. **Inference Configuration**: When using 4-step inference, configure correct `denoising_step_list`, recommended: `[1000, 750, 500, 250]`


## 📚 Related Resources

### Documentation Links
- **LightX2V Quick Start**: [Quick Start Documentation](https://lightx2v-zhcn.readthedocs.io/zh-cn/latest/getting_started/quickstart.html)
- **Model Conversion Tool**: [Conversion Tool Documentation](https://github.com/ModelTC/LightX2V/blob/main/tools/convert/readme_zh.md)
- **Online LoRA Loading**: [Configuration File Example](https://github.com/ModelTC/LightX2V/blob/main/configs/wan22/wan_moe_i2v_distil_with_lora.json)
- **Quantization Guide**: [Quantization Documentation](https://lightx2v-zhcn.readthedocs.io/zh-cn/latest/method_tutorials/quantization.html)
- **Model Structure**: [Model Structure Documentation](https://lightx2v-zhcn.readthedocs.io/zh-cn/latest/getting_started/model_structure.html)

### Related Models
- **Distilled Full Models**: [Wan2.2-Distill-Models](https://huggingface.co/lightx2v/Wan2.2-Distill-Models)
- **Wan2.2 Official Models**: [Wan2.2-Official-Models](https://huggingface.co/lightx2v/Wan2.2-Official-Models) - Contains high noise and low noise base models
- **Base Model (Wan-AI)**: [Wan2.2-I2V-A14B](https://huggingface.co/Wan-AI/Wan2.2-I2V-A14B)

## 🤝 Community & Support

- **GitHub Issues**: https://github.com/ModelTC/LightX2V/issues
- **HuggingFace**: https://huggingface.co/lightx2v/Wan2.2-Distill-Loras
- **LightX2V Homepage**: https://github.com/ModelTC/LightX2V

If you find this project helpful, please give us a ⭐ on [GitHub](https://github.com/ModelTC/LightX2V)