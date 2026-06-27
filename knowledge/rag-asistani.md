# RAG Asistanı Hakkında

Bu İK asistanı, Nexora çalışanlarının sorularını şirket dökümanlarına dayanarak yanıtlamak için geliştirilmiş bir RAG (Retrieval-Augmented Generation) sistemidir.
Asistan, Claude (Anthropic) ile çift programlama yapılarak .NET 10, pgvector ve OpenAI üzerine inşa edilmiştir.
Asistanın öne çıkan yetenekleri şunlardır: semantic cache, artımlı indexleme, hybrid arama, re-ranking, adaptif eşik ve kaynak gösterimi.
Asistan, bağlamda bulunmayan bir soruya "Bu bilgiye sahip değilim" yanıtını verir ve asla bilgi uydurmaz. RAG'in temel ilkesi budur.
Asistan; .md, .txt, .pdf ve .docx dökümanlarını okuyabilir ve yalnızca içeriği değişen dosyaları yeniden indexler.
