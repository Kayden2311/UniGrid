package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "TaskAttachment", schema = "dbo")
public class TaskAttachment {
    @Id
    @Column(name = "attachmentId", nullable = false)
    private UUID id;

    @Column(name = "taskId", nullable = false)
    private UUID taskId;

    @Nationalized
    @Column(name = "fileName")
    private String fileName;

    @Column(name = "fileSize")
    private Long fileSize;

    @Nationalized
    @Column(name = "fileURL", length = 500)
    private String fileURL;

    @Column(name = "uploadedBy")
    private UUID uploadedBy;

    @ColumnDefault("sysdatetime()")
    @Column(name = "uploadedAt")
    private Instant uploadedAt;


}