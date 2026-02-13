from unsloth import FastLanguageModel
import torch
from trl import SFTTrainer
from transformers import TrainingArguments
from datasets import load_dataset

# 1. Configuration
max_seq_length = 2048 # Gemma 3 supports 128k, but for fine-tuning, start small to save RAM
dtype = None # Auto detection (Float16/Bfloat16)
load_in_4bit = True # 4-bit quantization is mandatory for 27B efficiency

# 2. Load Model & Tokenizer
model, tokenizer = FastLanguageModel.from_pretrained(
    model_name = "google/gemma-3-27b-it", # The Instruct version
    max_seq_length = max_seq_length,
    dtype = dtype,
    load_in_4bit = load_in_4bit,
)

# 3. Add LoRA Adapters (The trainable layers)
model = FastLanguageModel.get_peft_model(
    model,
    r = 16, # Rank: keep small (8, 16, 32) for efficiency
    target_modules = ["q_proj", "k_proj", "v_proj", "o_proj",
                      "gate_proj", "up_proj", "down_proj"],
    lora_alpha = 16,
    lora_dropout = 0, # Optimized to 0 by Unsloth
    bias = "none",
    use_gradient_checkpointing = "unsloth", # Critical for memory saving
    random_state = 3407,
)

# 4. Format Dataset (Standard Alpaca/Chat format)
# You need a function that formats your data into the prompt style:
# <start_of_turn>user\n{PROMPT}<end_of_turn>\n<start_of_turn>model\n{RESPONSE}<end_of_turn>
alpaca_prompt = """<start_of_turn>user
{}<end_of_turn>
<start_of_turn>model
{}<end_of_turn>"""

def formatting_prompts_func(examples):
    inputs = examples["instruction"]
    outputs = examples["output"]
    texts = []
    for input, output in zip(inputs, outputs):
        text = alpaca_prompt.format(input, output) + tokenizer.eos_token
        texts.append(text)
    return { "text" : texts, }

dataset = load_dataset("yahma/alpaca-cleaned", split = "train")
dataset = dataset.map(formatting_prompts_func, batched = True)

# 5. Training Arguments
trainer = SFTTrainer(
    model = model,
    tokenizer = tokenizer,
    train_dataset = dataset,
    dataset_text_field = "text",
    max_seq_length = max_seq_length,
    args = TrainingArguments(
        per_device_train_batch_size = 2,
        gradient_accumulation_steps = 4,
        warmup_steps = 5,
        max_steps = 60, # Increase this for real training (e.g., 500+)
        learning_rate = 2e-4,
        fp16 = not torch.cuda.is_bf16_supported(),
        bf16 = torch.cuda.is_bf16_supported(),
        logging_steps = 1,
        optim = "adamw_8bit", # Saves optimizer memory
        output_dir = "gemma3_lora_outputs",
    ),
)

trainer.train()

# 6. Save ONLY the Adapters (Small files)
model.save_pretrained("gemma3_lora_adapters")
tokenizer.save_pretrained("gemma3_lora_adapters")