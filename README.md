# Payment Consumer

Este projeto consiste num consumer da fila de concilliations que é utilizado pelo projeto de API PIX disponível neste repositório: `https://github.com/pablodru/pix-stone`;

## Tecnologias 🔧

Para a construção do projeto foi utilizado as seguintes tecnologias:

- .Net: v8.0.202
- RabbitMQ
- PostgreSQL

## Instalação e Execução 🚀

Para rodar o projeto localmente, siga os seguinter passos:

1. Clone o repositório: `git clone https://github.com/pablodru/pix-stone-concilliation-consumer.git`;
2. Acesse o diretório do projeto: `cd pix-stone-payment-consumer`;
3. Certifique-se de ter o RabbitMQ e o PostgreSQL rodando em sua máquina e as credenciais de conexão com eles;
4. Rode o comando `dotnet restore`;
5. Rode o projeto com: `dotnet run`;
6. Depois disso, a aplicação estará consumindo a fila de Concilliations;

Nota: O consumer faz requisição a uma PSP, então certifique-se de ter um mock rodando na porta utilizada.
